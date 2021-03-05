// <copyright file="MediaContainer.cs" company="ne1410s">
// Copyright (c) ne1410s. All rights reserved.
// </copyright>

namespace AV.Core.Container
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using AV.Core.Common;
    using AV.Core.Primitives;
    using FFmpeg.AutoGen;

    /// <summary>
    /// A container capable of opening an input url,
    /// reading packets from it, decoding frames, seeking, and pausing and
    /// resuming network streams based on
    /// https://raw.githubusercontent.com/FFmpeg/FFmpeg/release/3.2/ffplay.c
    /// The method pipeline should be:
    /// 1. Set Options (or don't, for automatic options) and Initialize,
    /// 2. Perform continuous packet reads,
    /// 3. Perform continuous frame decodes
    /// 4. Perform continuous block materialization.
    /// </summary>
    /// <seealso cref="IDisposable" />
    public sealed unsafe class MediaContainer : IDisposable
    {
        /// <summary>
        /// The exception message no input context.
        /// </summary>
        private const string ExceptionMessageNoInputContext = "Stream InputContext has not been initialized.";

        /// <summary>
        /// The read synchronize root.
        /// </summary>
        private readonly object readSyncRoot = new ();

        /// <summary>
        /// The decode synchronize root.
        /// </summary>
        private readonly object decodeSyncRoot = new ();

        /// <summary>
        /// The convert synchronize root.
        /// </summary>
        private readonly object convertSyncRoot = new ();

        /// <summary>
        /// The stream read interrupt start time.
        /// When a read operation is started, this is set to the ticks of UTC now.
        /// </summary>
        private readonly AtomicDateTime streamReadInterruptStartTime = new (default);

        /// <summary>
        /// The signal to request the abortion of the following read operation.
        /// </summary>
        private readonly AtomicBoolean signalAbortReadsRequested = new (false);

        /// <summary>
        /// If set to true, it will reset the abort requested flag to false.
        /// </summary>
        private readonly AtomicBoolean signalAbortReadsAutoReset = new (false);

        /// <summary>
        /// The stream read interrupt callback.
        /// Used to detect read timeouts.
        /// </summary>
        private readonly AVIOInterruptCB_callback streamReadInterruptCallback;

        /// <summary>
        /// The custom media input stream.
        /// </summary>
        private IMediaInputStream customInputStream;

        /// <summary>
        /// The custom input stream read callback.
        /// </summary>
        private avio_alloc_context_read_packet customInputStreamRead;

        /// <summary>
        /// The custom input stream seek callback.
        /// </summary>
        private avio_alloc_context_seek customInputStreamSeek;

        /// <summary>
        /// The custom input stream context.
        /// </summary>
        private AVIOContext* customInputStreamContext;

        /// <summary>
        /// Hold the value for the internal property with the same name.
        /// Picture attachments are required when video streams support them
        /// and these attached packets must be read before reading the first frame
        /// of the stream and after seeking.
        /// </summary>
        private bool requiresPictureAttachments = true;

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaContainer"/> class.
        /// </summary>
        /// <param name="mediaSource">The media URL.</param>
        /// <param name="config">The container configuration options.</param>
        /// <exception cref="ArgumentNullException">Media Source cannot be null.</exception>
        public MediaContainer(string mediaSource, ContainerConfiguration config)
        {
            // Argument Validation
            if (string.IsNullOrWhiteSpace(mediaSource))
            {
                throw new ArgumentNullException($"{nameof(mediaSource)}");
            }

            // Initialize the library (if not already done)
            FFInterop.Initialize(null, FFmpegLoadMode.FullFeatures);

            // Create the options object and setup some initial properties
            this.MediaSource = mediaSource;
            this.Configuration = config ?? new ContainerConfiguration();
            this.streamReadInterruptCallback = this.OnStreamReadInterrupt;

            // drop the protocol prefix if it is redundant
            var protocolPrefix = this.Configuration.ProtocolPrefix;
            if (string.IsNullOrWhiteSpace(this.MediaSource) == false
                && string.IsNullOrWhiteSpace(protocolPrefix) == false
                && this.MediaSource.Trim().StartsWith($"{protocolPrefix}:", StringComparison.OrdinalIgnoreCase))
            {
                protocolPrefix = null;
            }

            this.Configuration.ProtocolPrefix = protocolPrefix;
        }

        /// <summary>
        /// Initialises a new instance of the <see cref="MediaContainer"/> class.
        /// </summary>
        /// <param name="inputStream">The input stream.</param>
        /// <param name="config">The configuration.</param>
        public MediaContainer(
            IMediaInputStream inputStream,
            ContainerConfiguration config = null)
        {
            // Argument Validation
            if (inputStream == null)
            {
                throw new ArgumentNullException($"{nameof(inputStream)}");
            }

            // Validate the stream pseudo Url
            var mediaSourceUrl = inputStream.StreamUri?.ToString();
            if (string.IsNullOrWhiteSpace(mediaSourceUrl))
            {
                throw new ArgumentNullException($"{nameof(inputStream)}.{nameof(inputStream.StreamUri)}");
            }

            // Initialize the library (if not already done)
            FFInterop.Initialize(null, FFmpegLoadMode.FullFeatures);

            this.MediaSource = mediaSourceUrl;
            this.customInputStream = inputStream;
            this.Configuration = config ?? new ContainerConfiguration();
        }

        /// <summary>
        /// Gets a value indicating whether to detect redundant Dispose calls.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets the media URL. This is the input url, file or device that is read
        /// by this container.
        /// </summary>
        public string MediaSource { get; }

        /// <summary>
        /// Gets the container and demuxer initialization and configuration
        /// options, applied when creating an instance of the container.
        /// After container creation, changing the configuration options passed
        /// in the constructor has no effect.
        /// </summary>
        public ContainerConfiguration Configuration { get; }

        /// <summary>
        /// Gets options that applied before initializing media components and
        /// their corresponding codecs. Once the container has created the media
        /// components, changing these options may produce side effects and is
        /// not supported or recommended.
        /// </summary>
        public MediaOptions MediaOptions { get; } = new MediaOptions();

        /// <summary>
        /// Gets stream, chapter and program info held by this container.
        /// This property is null if the the stream has not been opened.
        /// </summary>
        public MediaInfo MediaInfo { get; private set; }

        /// <summary>
        /// Gets the name of the media format.
        /// </summary>
        public string MediaFormatName { get; private set; }

        /// <summary>
        /// Gets the media bit rate (bits / second). Returns 0 if not available.
        /// </summary>
        public long MediaBitRate => this.MediaInfo?.BitRate ?? 0;

        /// <summary>
        /// Gets the metadata of the media file when the stream is initialized.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; private set; }

        /// <summary>
        /// Gets a value indicating whether Input Context has been initialised.
        /// </summary>
        public bool IsInitialized => this.InputContext != null;

        /// <summary>
        /// Gets a value indicating whether this instance is open.
        /// </summary>
        public bool IsOpen => this.IsInitialized && this.Components.Count > 0;

        /// <summary>
        /// Gets a value indicating whether End Of File situation is reached.
        /// </summary>
        public bool IsAtEndOfStream { get; private set; }

        /// <summary>
        /// Gets the byte position at which the stream is being read.
        /// Please note that this property gets updated after every Read.
        /// For multi-file streams, get the position of the current file only.
        /// </summary>
        public long StreamPosition
        {
            get
            {
                if (this.InputContext == null || InputContext->pb == null)
                {
                    return 0;
                }

                return InputContext->pb->pos;
            }
        }

        /// <summary>
        /// Gets the size in bytes of the current stream being read.
        /// For multi-file streams, get the size of the current file only.
        /// </summary>
        public long MediaStreamSize
        {
            get
            {
                if (this.InputContext == null || InputContext->pb == null)
                {
                    return 0;
                }

                var size = ffmpeg.avio_size(InputContext->pb);
                return size > 0 ? size : 0;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the underlying media is seekable.
        /// </summary>
        public bool IsStreamSeekable => this.customInputStream?.CanSeek ?? (this.Components?.Seekable?.Duration.Ticks ?? 0) > 0;

        /// <summary>
        /// Gets a value indicating whether this container represents live media.
        /// If the stream is classified as a network stream and it is not
        /// seekable, then this property will return true.
        /// </summary>
        public bool IsLiveStream => this.IsNetworkStream && !this.IsStreamSeekable;

        /// <summary>
        /// Gets a value indicating whether the input stream is a network stream.
        /// If the format name is rtp, rtsp, or sdp or if the url starts with
        /// udp:, http:, https:, tcp:, or rtp: then this property is true.
        /// </summary>
        public bool IsNetworkStream { get; private set; }

        /// <summary>
        /// Gets direct access to the stream individual Media components.
        /// </summary>
        public MediaComponentSet Components { get; } = new MediaComponentSet();

        /// <summary>
        /// Gets direct access to that necessary to handle non-media packets.
        /// </summary>
        public DataComponentSet Data { get; } = new DataComponentSet();

        /// <summary>
        /// Gets a value indicating whether reads are in the aborted state.
        /// </summary>
        public bool IsReadAborted => this.signalAbortReadsRequested.Value;

        /// <summary>
        /// Gets a reference to the input context.
        /// </summary>
        internal AVFormatContext* InputContext { get; private set; } = null;

        /// <summary>
        /// Gets or sets a value indicating whether picture attachments are
        /// required when video streams support them and these attached packets
        /// must be read before reading the first frame of the stream and after
        /// seeking. This property is not part of the public API and is meant
        /// more for internal purposes.
        /// </summary>
        private bool StateRequiresPictureAttachments
        {
            get
            {
                var canRequireAttachments = this.Components.HasVideo &&
                    this.Components.Video.IsStillPictures;

                return canRequireAttachments && this.requiresPictureAttachments;
            }

            set
            {
                var canRequireAttachments = this.Components.HasVideo &&
                    this.Components.Video.IsStillPictures;

                this.requiresPictureAttachments = canRequireAttachments && value;
            }
        }

        /// <summary>
        /// Initializes the container and its input context, extracting stream
        /// information. ontainer configuration passed on the constructor is
        /// applied. This method must be called to make the container usable.
        /// </summary>
        public void Initialize()
        {
            lock (this.readSyncRoot)
            {
                // Initialize the Input Format Context and Input Stream Context
                this.customInputStream?.OnInitializing?.Invoke(this.Configuration, this.MediaSource);
                this.StreamInitialize();
            }
        }

        /// <summary>
        /// Opens the individual stream components on the existing input context
        /// in order to start reading packets. Any Media Options must be set
        /// before this method is called.
        /// </summary>
        public void Open()
        {
            lock (this.readSyncRoot)
            {
                if (this.IsDisposed)
                {
                    throw new ObjectDisposedException(nameof(MediaContainer));
                }

                if (this.InputContext == null)
                {
                    throw new InvalidOperationException(ExceptionMessageNoInputContext);
                }

                if (this.IsOpen)
                {
                    throw new InvalidOperationException("The stream components are already open.");
                }

                this.StreamOpen();
            }
        }

        /// <summary>
        /// Seeks to the specified position in the main stream component.
        /// Returns the keyframe on or before the specified position. Most of
        /// the time you will need to keep reading packets and receiving frames
        /// to reach the exact position. Pass TimeSpan.MinValue to seek to the
        /// beginning of the stream.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns>
        /// The list of media frames.
        /// </returns>
        /// <exception cref="InvalidOperationException">No inited input context.</exception>
        public MediaFrame Seek(TimeSpan position)
        {
            lock (this.readSyncRoot)
            {
                if (this.IsDisposed)
                {
                    throw new ObjectDisposedException(nameof(MediaContainer));
                }

                if (this.InputContext == null)
                {
                    throw new InvalidOperationException(ExceptionMessageNoInputContext);
                }

                return this.StreamSeek(position);
            }
        }

        /// <summary>
        /// Reads the next available packet, sending the packet to the
        /// internal media component. It also sets IsAtEndOfStream property.
        /// Returns the media type if the packet was accepted by any of the
        /// components. Returns None if the packet was not accepted by any of
        /// the media components or if reading failed (i.e. End of stream
        /// already or read error). Packets are queued internally. To dequeue
        /// them you need to call the receive frames method of each component
        /// until the packet buffer count becomes 0.
        /// </summary>
        /// <returns>The media type of the packet that was read.</returns>
        /// <exception cref="InvalidOperationException">No inited input context.</exception>
        /// <exception cref="MediaContainerException">When a read error occurs.</exception>
        public MediaType Read()
        {
            lock (this.readSyncRoot)
            {
                if (this.IsDisposed)
                {
                    throw new ObjectDisposedException(nameof(MediaContainer));
                }

                if (this.InputContext == null)
                {
                    throw new InvalidOperationException(ExceptionMessageNoInputContext);
                }

                return this.StreamRead();
            }
        }

        /// <summary>
        /// Decodes the next available packet in the packet queue for each of
        /// the components. Returns the list of decoded frames. The list of 0 or
        /// more decoded frames is returned in ascending StartTime order. Packet
        /// may contain 0 or more frames. Once the frame source objects are
        /// returned, you are responsible for calling the Dispose method on them
        /// to free the underlying FFmpeg frame. Note that even after releasing
        /// them you can still use the managed properties. If you intend on
        /// Converting the frames to usable media frames (with Convert) you must
        /// not release the frame. Specify the release input argument as true
        /// and the frame will be automatically freed from memory.
        /// </summary>
        /// <returns>The list of media frames.</returns>
        public IList<MediaFrame> Decode()
        {
            lock (this.decodeSyncRoot)
            {
                if (this.IsDisposed)
                {
                    throw new ObjectDisposedException(nameof(MediaContainer));
                }

                if (this.InputContext == null)
                {
                    throw new InvalidOperationException(ExceptionMessageNoInputContext);
                }

                var result = new List<MediaFrame>(4);
                MediaFrame frame;
                foreach (var component in this.Components.All)
                {
                    frame = component.ReceiveNextFrame();
                    if (frame != null)
                    {
                        result.Add(frame);
                    }
                }

                result.Sort();
                return result;
            }
        }

        /// <summary>
        /// Performs audio, video and subtitle conversions on the decoded input
        /// frame so data can be used as a Frame. Please note that if the output
        /// is passed as a reference. This works as follows: if the output
        /// reference is null it will be automatically instantiated and returned
        /// by this function. This enables to  either instantiate or reuse a
        /// previously allocated Frame. This is important because buffer
        /// allocations are expensive operations and this allows you to perform
        /// the allocation once and continue reusing the same buffer.
        /// </summary>
        /// <param name="input">The raw frame source. Has to be compatible with
        /// the target. (e.g. use VideoFrameSource to convert to VideoFrame).</param>
        /// <param name="output">The target frame. Has to be compatible with the source.</param>
        /// <param name="releaseInput">if set to <c>true</c> releases the raw
        /// frame source from unmanaged memory.</param>
        /// <param name="previousBlock">The previous block from which to extract
        /// timing information in case it is missing.</param>
        /// <returns>True if successful. False otherwise. </returns>
        /// <exception cref="InvalidOperationException">No inited input context.</exception>
        /// <exception cref="MediaContainerException">MediaType.</exception>
        /// <exception cref="ArgumentNullException">input null.</exception>
        /// <exception cref="ArgumentException">input.</exception>
        public bool Convert(
            MediaFrame input,
            ref MediaBlock output,
            bool releaseInput,
            MediaBlock previousBlock)
        {
            lock (this.convertSyncRoot)
            {
                if (this.IsDisposed)
                {
                    throw new ObjectDisposedException(nameof(MediaContainer));
                }

                if (this.InputContext == null)
                {
                    throw new InvalidOperationException(ExceptionMessageNoInputContext);
                }

                // Check the input parameters
                if (input == null)
                {
                    throw new ArgumentNullException($"{nameof(input)} cannot be null.");
                }

                if (input.IsStale)
                {
                    throw new ArgumentException(
                        $"The {nameof(input)} {nameof(MediaFrame)} ({input.MediaType}) has already been released (it's stale).");
                }

                try
                {
                    return input.MediaType switch
                    {
                        MediaType.Video => this.Components.HasVideo && this.Components.Video.MaterializeFrame(input, ref output, previousBlock),
                        _ => throw new MediaContainerException($"Unable to materialize frame of {nameof(MediaType)} {input.MediaType}"),
                    };
                }
                finally
                {
                    if (releaseInput)
                    {
                        input.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Signals the packet reading operations to abort immediately.
        /// </summary>
        /// <param name="reset">if set to true, the read interrupt will reset
        /// the aborted state automatically.</param>
        public void SignalAbortReads(bool reset)
        {
            if (this.IsDisposed)
            {
                return;
            }

            this.signalAbortReadsAutoReset.Value = reset;
            this.signalAbortReadsRequested.Value = true;
        }

        /// <summary>
        /// Signals the state for read operations to stop being aborted.
        /// </summary>
        public void SignalResumeReads()
        {
            throw new NotSupportedException("The Container does not support resuming the InputContext from aborted reads yet.");
        }

        /// <summary>
        /// Recreates the components using the selected streams in
        /// <see cref="MediaOptions" />. If the newly set streams are null these
        /// components are removed and disposed. All selected stream components
        /// are recreated.
        /// </summary>
        /// <returns>The registered component types.</returns>
        public MediaType[] UpdateComponents()
        {
            if (this.IsDisposed || this.InputContext == null)
            {
                return Array.Empty<MediaType>();
            }

            lock (this.readSyncRoot)
            {
                lock (this.decodeSyncRoot)
                {
                    lock (this.convertSyncRoot)
                    {
                        // Open the suitable streams as components.
                        // Throw if no audio and/or video streams are found
                        return this.StreamCreateComponents();
                    }
                }
            }
        }

        /// <summary>
        /// Closes the input context immediately releasing all resources.
        /// This method is equivalent to calling the dispose method.
        /// </summary>
        public void Close()
        {
            this.Dispose();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (this.readSyncRoot)
            {
                lock (this.decodeSyncRoot)
                {
                    lock (this.convertSyncRoot)
                    {
                        if (this.IsDisposed)
                        {
                            return;
                        }

                        this.Components.Dispose();
                        if (this.InputContext != null)
                        {
                            this.SignalAbortReads(false);
                            var inputContextPtr = this.InputContext;
                            ffmpeg.avformat_close_input(&inputContextPtr);

                            // Handle freeing of Custom Stream Context
                            if (this.customInputStreamContext != null)
                            {
                                // free the allocated buffer
                                ffmpeg.av_freep(&customInputStreamContext->buffer);

                                // free the stream context
                                var customInputContext = this.customInputStreamContext;
                                ffmpeg.av_freep(&customInputContext);
                                this.customInputStreamContext = null;
                            }

                            // Clear Custom Input fields
                            this.customInputStreamRead = null;
                            this.customInputStreamSeek = null;
                            this.customInputStream?.Dispose();
                            this.customInputStream = null;

                            // Clear the input context
                            this.InputContext = null;
                        }

                        this.IsDisposed = true;
                    }
                }
            }
        }

        /// <summary>
        /// Initializes the input context to start read operations.
        /// This does NOT create the stream components and therefore, there
        /// needs to be a call to the Open method.
        /// </summary>
        /// <exception cref="InvalidOperationException">The input context has
        /// already been initialized.</exception>
        /// <exception cref="MediaContainerException">When an error initialising
        /// the stream occurs.</exception>
        private void StreamInitialize()
        {
            if (this.IsInitialized)
            {
                throw new InvalidOperationException("The input context has already been initialized.");
            }

            // Retrieve the input format (null = auto for default)
            AVInputFormat* inputFormat = null;
            if (string.IsNullOrWhiteSpace(this.Configuration.ForcedInputFormat) == false)
            {
                inputFormat = ffmpeg.av_find_input_format(this.Configuration.ForcedInputFormat);
                if (inputFormat == null)
                {
                    //TODO: Warn
                    ////$"Format '{this.Configuration.ForcedInputFormat}' not found. Will use automatic format detection.");
                }
            }

            try
            {
                // Create the input format context, and open the input based on
                // the provided format options.
                using (var privateOptions = new FFDictionary(this.Configuration.PrivateOptions))
                {
                    if (privateOptions.HasKey(ContainerConfiguration.ScanAllPmts) == false)
                    {
                        privateOptions.Set(ContainerConfiguration.ScanAllPmts, "1", true);
                    }

                    // Create the input context
                    this.StreamInitializeInputContext();

                    // Try to open the input
                    var inputContextPtr = this.InputContext;

                    // Open the input file

                    // Prepare the open Url
                    var prefix = string.IsNullOrWhiteSpace(this.Configuration.ProtocolPrefix) ?
                        string.Empty : $"{this.Configuration.ProtocolPrefix.Trim()}:";

                    var openUrl = $"{prefix}{this.MediaSource}";

                    // If there is a custom input stream, set it up.
                    if (this.customInputStream != null)
                    {
                        // don't pass a Url because it will be a custom stream
                        openUrl = string.Empty;

                        // Setup the necessary context callbacks
                        this.customInputStreamRead = this.customInputStream.Read;
                        this.customInputStreamSeek = this.customInputStream.Seek;

                        // Allocate the read buffer
                        var inputBuffer = (byte*)ffmpeg.av_malloc((ulong)this.customInputStream.ReadBufferLength);
                        this.customInputStreamContext = ffmpeg.avio_alloc_context(
                            inputBuffer, this.customInputStream.ReadBufferLength, 0, null, this.customInputStreamRead, null, this.customInputStreamSeek);

                        // Set the seekable flag based on the custom input stream implementation
                        customInputStreamContext->seekable = this.customInputStream.CanSeek ? 1 : 0;

                        // Assign the AVIOContext to the input context
                        inputContextPtr->pb = this.customInputStreamContext;
                    }

                    // We set the start of the read operation time so timeouts
                    // can be detected and we open the URL so the input context
                    // can be initialized.
                    this.streamReadInterruptStartTime.Value = DateTime.UtcNow;
                    var privateOptionsRef = privateOptions.Pointer;

                    // Open the input and pass the private options dictionary
                    var openResult = ffmpeg.avformat_open_input(&inputContextPtr, openUrl, inputFormat, &privateOptionsRef);
                    privateOptions.UpdateReference(privateOptionsRef);
                    this.InputContext = inputContextPtr;

                    // Validate the open operation
                    if (openResult < 0)
                    {
                        throw new MediaContainerException($"Could not open '{this.MediaSource}'. "
                            + $"Error {openResult}: {FFInterop.DecodeMessage(openResult)}");
                    }

                    // Set some general properties
                    this.MediaFormatName = Utilities.PtrToStringUTF8(InputContext->iformat->name);

                    // If there are any options left in the dictionary, it means
                    // they did not get used (invalid options).
                    // Output the invalid options as warnings
                    privateOptions.Remove(ContainerConfiguration.ScanAllPmts);
                    var currentEntry = privateOptions.First();
                    while (currentEntry?.Key != null)
                    {
                        //TODO: Warn
                        ////$"Invalid input option: '{currentEntry.Key}'");
                        currentEntry = privateOptions.Next(currentEntry);
                    }
                }

                ffmpeg.av_format_inject_global_side_data(this.InputContext);

                // This is useful for file formats with no headers such as MPEG.
                // This function also computes the real frame-rate in case of
                // MPEG-2 repeat frame mode.
                if (ffmpeg.avformat_find_stream_info(this.InputContext, null) < 0)
                {
                    //TODO: Warn
                    ////$"{this.MediaSource}: could not read stream information.");
                }

                // HACK: From ffplay.c: maybe should not use avio_feof() to test for the end
                if (InputContext->pb != null)
                {
                    InputContext->pb->eof_reached = 0;
                }

                // Setup initial state variables
                this.Metadata = FFDictionary.ToDictionary(InputContext->metadata);

                // If read_play is set, it is only relevant to network streams
                this.IsNetworkStream = false;
                if (InputContext->iformat->read_play.Pointer != IntPtr.Zero)
                {
                    this.IsNetworkStream = true;

                    // The following line seems to have negative or no effect.
                    // Safe to comment out as the read thread will always try to
                    // read packets depending on the state of the buffer and not
                    // the state of the playback itself.
                    // It also has caused problems with RTSP streams. See #43
                    // and possibly the root cause of #415
                    // ffmpeg.av_read_play(InputContext)
                }

                if (this.IsNetworkStream == false && Uri.TryCreate(
                    this.MediaSource,
                    UriKind.RelativeOrAbsolute,
                    out var uri))
                {
                    try
                    {
                        this.IsNetworkStream = uri.IsFile == false;
                    }
                    catch
                    {
                        this.IsNetworkStream = true;
                    }
                }

                // Extract the Media Info
                this.MediaInfo = new MediaInfo(this);

                // Extract detailed media information and set the default streams to the
                // best available ones.
                foreach (var s in this.MediaInfo.BestStreams)
                {
                    if (s.Key == AVMediaType.AVMEDIA_TYPE_VIDEO)
                    {
                        this.MediaOptions.VideoStream = s.Value;
                    }
                }

                // Prevent creation of unavailable audio or video components
                if (FFLibrary.LibSWScale.IsLoaded == false)
                {
                    //TODO: Error
                    ////$"We totes need that lib!;
                }

                this.customInputStream?.OnInitialized?.Invoke(
                    inputFormat,
                    this.InputContext,
                    this.MediaInfo);
            }
            catch (Exception ex)
            {
                //TODO: Error
                ////$"Fatal error initializing {nameof(MediaContainer)} instance.", ex);
                this.Close();
                throw;
            }
        }

        /// <summary>
        /// Initializes the InputContext and applies format options.
        /// https://www.ffmpeg.org/ffmpeg-formats.html#Format-Options.
        /// </summary>
        private void StreamInitializeInputContext()
        {
            // Allocate the input context and save it
            this.InputContext = ffmpeg.avformat_alloc_context();

            // Setup an interrupt callback to detect read timeouts
            this.signalAbortReadsRequested.Value = false;
            this.signalAbortReadsAutoReset.Value = true;
            InputContext->interrupt_callback.callback = this.streamReadInterruptCallback;
            InputContext->interrupt_callback.opaque = this.InputContext;

            // Acquire the format options to be applied
            var opts = this.Configuration.GlobalOptions;

            // Apply the options
            if (opts.EnableReducedBuffering)
            {
                InputContext->avio_flags |= ffmpeg.AVIO_FLAG_DIRECT;
            }

            if (opts.PacketSize != default)
            {
                InputContext->packet_size = System.Convert.ToUInt32(opts.PacketSize);
            }

            if (opts.ProbeSize != default)
            {
                InputContext->probesize = opts.ProbeSize <= 32 ? 32 : opts.ProbeSize;
            }

            // Flags
            InputContext->flags |= opts.FlagDiscardCorrupt ? ffmpeg.AVFMT_FLAG_DISCARD_CORRUPT : InputContext->flags;
            InputContext->flags |= opts.FlagEnableFastSeek ? ffmpeg.AVFMT_FLAG_FAST_SEEK : InputContext->flags;
            InputContext->flags |= opts.FlagEnableLatmPayload ? ffmpeg.AVFMT_FLAG_MP4A_LATM : InputContext->flags;
            InputContext->flags |= opts.FlagEnableNoFillIn ? ffmpeg.AVFMT_FLAG_NOFILLIN : InputContext->flags;
            InputContext->flags |= opts.FlagGeneratePts ? ffmpeg.AVFMT_FLAG_GENPTS : InputContext->flags;
            InputContext->flags |= opts.FlagIgnoreDts ? ffmpeg.AVFMT_FLAG_IGNDTS : InputContext->flags;
            InputContext->flags |= opts.FlagIgnoreIndex ? ffmpeg.AVFMT_FLAG_IGNIDX : InputContext->flags;
            InputContext->flags |= opts.FlagKeepSideData ? ffmpeg.AVFMT_FLAG_KEEP_SIDE_DATA : InputContext->flags;
            InputContext->flags |= opts.FlagNoBuffer ? ffmpeg.AVFMT_FLAG_NOBUFFER : InputContext->flags;
            InputContext->flags |= opts.FlagSortDts ? ffmpeg.AVFMT_FLAG_SORT_DTS : InputContext->flags;
            InputContext->flags |= opts.FlagStopAtShortest ? ffmpeg.AVFMT_FLAG_SHORTEST : InputContext->flags;

            InputContext->seek2any = opts.SeekToAny ? 1 : 0;

            // Handle analyze duration overrides
            if (opts.MaxAnalyzeDuration != default)
            {
                InputContext->max_analyze_duration = opts.MaxAnalyzeDuration <= TimeSpan.Zero ? 0 :
                    System.Convert.ToInt64(opts.MaxAnalyzeDuration.TotalSeconds * ffmpeg.AV_TIME_BASE);
            }

            if (!string.IsNullOrWhiteSpace(opts.ProtocolWhitelist))
            {
                InputContext->protocol_whitelist = FFInterop.StringToBytePointerUTF8(opts.ProtocolWhitelist);
            }
        }

        /// <summary>
        /// Opens the individual stream components to start reading packets.
        /// </summary>
        private void StreamOpen()
        {
            // Open the best suitable streams. Throw if no audio and/or video
            // streams are found
            this.StreamCreateComponents();
        }

        /// <summary>
        /// Creates and assigns a component of the given type using the
        /// specified stream information. If stream information is null, or the
        /// component is disabled, then the component is removed.
        /// </summary>
        /// <param name="t">The Media Type.</param>
        /// <param name="stream">The stream info. Set to null to remove.</param>
        /// <returns>The media type created; None for failed creation.</returns>
        private MediaType StreamCreateComponent(MediaType t, StreamInfo stream)
        {
            try
            {
                // Remove the existing component if it exists already
                if (this.Components[t] != null)
                {
                    this.Components.RemoveComponent(t);
                }

                // Instantiate component
                if (stream != null && stream.CodecType == (AVMediaType)t)
                {
                    if (t == MediaType.Video)
                    {
                        this.Components.AddComponent(new VideoComponent(this, stream.StreamIndex));
                    }
                }
            }
            catch
            {
                //TODO: Error
                ////$"Unable to initialize {t} component.", ex);
            }

            return this.Components[t] != null ? t : MediaType.None;
        }

        /// <summary>
        /// Creates the stream components according to the specified streams in
        /// the current media options. Then it initializes the components of the
        /// correct type each.
        /// </summary>
        /// <returns>The component media types that are available.</returns>
        /// <exception cref="MediaContainerException">Info.</exception>
        private MediaType[] StreamCreateComponents()
        {
            // Apply Media Options by selecting the desired components
            this.StreamCreateComponent(MediaType.Video, this.MediaOptions.VideoStream);

            // Verify we have at least 1 stream component to work with.
            if (this.Components.HasVideo == false)
            {
                throw new MediaContainerException($"{this.MediaSource}: No video streams found to decode.");
            }

            // Initially and depending on the video component, require picture
            // attachments. Picture attachments are only required after the
            // first read or after a seek.
            this.StateRequiresPictureAttachments = true;

            // Return the registered component types
            return this.Components.MediaTypes.ToArray();
        }

        /// <summary>
        /// Reads the next packet in the underlying stream and queues in the
        /// corresponding media component. Returns None of no packet was read.
        /// </summary>
        /// <returns>The type of media packet that was read.</returns>
        /// <exception cref="InvalidOperationException">Initialize.</exception>
        /// <exception cref="MediaContainerException">Raised when an error
        /// reading from the stream occurs.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MediaType StreamRead()
        {
            // Check the context has been initialized
            if (this.IsOpen == false)
            {
                throw new InvalidOperationException($"Please call the {nameof(this.Open)} method before attempting this operation.");
            }

            if (this.IsReadAborted)
            {
                return MediaType.None;
            }

            if (this.StateRequiresPictureAttachments)
            {
                var attachedPacket = MediaPacket.ClonePacket(&this.Components.Video.Stream->attached_pic);
                if (attachedPacket != null)
                {
                    this.Components.Video.SendPacket(attachedPacket);
                    this.Components.Video.SendEmptyPacket();
                }

                this.StateRequiresPictureAttachments = false;
            }

            // Allocate the packet to read
            var readPacket = MediaPacket.CreateReadPacket();
            this.streamReadInterruptStartTime.Value = DateTime.UtcNow;
            var readResult = ffmpeg.av_read_frame(this.InputContext, readPacket.Pointer);

            if (readResult < 0)
            {
                // Handle failed packet reads. We don't need the packet anymore
                readPacket.Dispose();
                readPacket = null;

                // Detect end of file (makes the readers enter draining mode)
                if (readResult == ffmpeg.AVERROR_EOF || ffmpeg.avio_feof(InputContext->pb) != 0)
                {
                    // Send the decoders empty packets at the EOF
                    if (this.IsAtEndOfStream == false)
                    {
                        this.Components.SendEmptyPackets();
                    }

                    this.IsAtEndOfStream = true;
                    return MediaType.None;
                }

                if (InputContext->pb != null && InputContext->pb->error != 0)
                {
                    throw new MediaContainerException($"Input has produced an error. Error Code {readResult}, {FFInterop.DecodeMessage(readResult)}");
                }
            }
            else
            {
                this.IsAtEndOfStream = false;
            }

            // Check if able to feed the packet. If not, simply discard it
            if (readPacket == null)
            {
                return MediaType.None;
            }

            // Push a data packet if its not a media component.
            if (this.Data.TryHandleDataPacket(this, readPacket))
            {
                return MediaType.None;
            }

            var componentType = this.Components.SendPacket(readPacket);

            // Discard the packet -- it was not accepted by any component
            if (componentType == MediaType.None)
            {
                readPacket.Dispose();
            }
            else
            {
                return componentType;
            }

            return MediaType.None;
        }

        /// <summary>
        /// The interrupt callback to handle stream reading timeouts.
        /// </summary>
        /// <param name="opaque">A pointer to the format input context.</param>
        /// <returns>0 for OK, 1 for error (timeout).</returns>
        private int OnStreamReadInterrupt(void* opaque)
        {
            const int ErrorResult = 1;
            const int OkResult = 0;

            // Check if a forced quit was triggered
            if (this.signalAbortReadsRequested.Value)
            {
                if (this.signalAbortReadsAutoReset.Value)
                {
                    this.signalAbortReadsRequested.Value = false;
                }

                return ErrorResult;
            }

            var nowTicks = DateTime.UtcNow.Ticks;

            // We use Interlocked read because in 32 bits it takes 2 trips!
            var start = this.streamReadInterruptStartTime.Value;
            var timeDifference = TimeSpan.FromTicks(nowTicks - start.Ticks);

            if (this.Configuration.ReadTimeout.Ticks < 0 || timeDifference.Ticks <= this.Configuration.ReadTimeout.Ticks)
            {
                return OkResult;
            }

            //TODO: Error
            ////$"{nameof(this.OnStreamReadInterrupt)} timed out with  {timeDifference.Format()}");
            return ErrorResult;
        }

        /// <summary>
        /// Seeks to the closest lesser / equal key frame on the main component.
        /// </summary>
        /// <param name="requestedPosition">The target time.</param>
        /// <returns>The seeked media frame.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MediaFrame StreamSeek(TimeSpan requestedPosition)
        {
            // Select the seeking component
            var comp = this.Components.Seekable;
            if (comp == null || !this.IsStreamSeekable)
            {
                //TODO: Warn
                ////"Unable to seek. Underlying stream does not support seeking.");
                return null;
            }

            // Adjust the requested position
            if (requestedPosition < comp.StartTime || requestedPosition < TimeSpan.Zero)
            {
                return this.StreamSeekToStart();
            }

            if (requestedPosition > comp.EndTime)
            {
                requestedPosition = comp.EndTime;
            }

            // Stream seeking by seeking component
            // The backward flag means seek to at MOST the target position
            var timeBase = comp.Stream->time_base;

            // The relative target time keeps track of where to seek.
            // if the seeking ends up AFTER the target, we decrement this time
            // and try the seek again by subtracting 1 second from it.
            var streamSeekRelativeTime = requestedPosition;

            // Perform long seeks until we end up with a relative target time
            // where decoding of frames before or on target time is possible.
            var isAtStartOfStream = false;
            int seekResult;
            MediaFrame frame = null;
            while (isAtStartOfStream == false)
            {
                // Compute seek target, mostly based on the relative Target Time
                var seekTimestamp = streamSeekRelativeTime.ToLong(timeBase);

                // Perform the seek. There is also avformat_seek_file which is
                // the older version of av_seek_frame. Check if we are seeking
                // before the start of the stream in this cycle. If so, simply
                // seek to the beginning of the stream. Otherwise, seek normally.
                if (this.IsReadAborted)
                {
                    seekResult = ffmpeg.AVERROR_EXIT;
                }
                else
                {
                    // Reset Interrupt start time
                    this.streamReadInterruptStartTime.Value = DateTime.UtcNow;

                    // check if we have seeked before the start of the stream
                    if (streamSeekRelativeTime.Ticks <= comp.StartTime.Ticks)
                    {
                        seekTimestamp = comp.StartTime.ToLong(comp.Stream->time_base);
                        isAtStartOfStream = true;
                    }

                    seekResult = ffmpeg.av_seek_frame(this.InputContext, comp.StreamIndex, seekTimestamp, ffmpeg.AVSEEK_FLAG_BACKWARD);
                }

                // Flush the buffered packets and codec on every seek.
                this.Components.ClearQueuedPackets(flushBuffers: true);
                this.StateRequiresPictureAttachments = true;
                this.IsAtEndOfStream = false;

                // Ensure we had a successful seek operation
                if (seekResult < 0)
                {
                    //TODO: Error
                    ////$"SEEK R: Elapsed: {startTime.FormatElapsed()} | Seek operation failed. Error code {seekResult}, {FFInterop.DecodeMessage(seekResult)}");
                    break;
                }

                // Get the main component position
                frame = this.StreamPositionDecode(comp);

                // If we could not read a frame from the main component or
                // if the first decoded frame is past the target time
                // try again with a lower relative time.
                if (frame == null || frame.StartTime.Ticks > requestedPosition.Ticks)
                {
                    streamSeekRelativeTime = streamSeekRelativeTime.Subtract(TimeSpan.FromSeconds(1));
                    frame?.Dispose();
                    frame = null;
                    continue;
                }

                // At this point frame contains the
                // prior keyframe to the seek target
                break;
            }

            return frame;
        }

        /// <summary>
        /// Seeks to the position at the start of the stream.
        /// </summary>
        /// <returns>The first frame of the main component.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MediaFrame StreamSeekToStart()
        {
            var main = this.Components.Seekable;
            var seekTarget = main.StartTime == TimeSpan.MinValue
                ? ffmpeg.AV_NOPTS_VALUE
                : main.StartTime.ToLong(main.Stream->time_base);

            this.streamReadInterruptStartTime.Value = DateTime.UtcNow;

            // Execute the seek to start of main component
            var seekResult = ffmpeg.av_seek_frame(this.InputContext, main.StreamIndex, seekTarget, ffmpeg.AVSEEK_FLAG_BACKWARD);

            // Flush packets, state, and codec buffers
            this.Components.ClearQueuedPackets(flushBuffers: true);
            this.StateRequiresPictureAttachments = true;
            this.IsAtEndOfStream = false;

            if (seekResult >= 0)
            {
                return this.StreamPositionDecode(main);
            }

            //TODO: Warn
            ////$"SEEK 0: {nameof(this.StreamSeekToStart)} operation failed. Error code {seekResult}: {FFInterop.DecodeMessage(seekResult)}");

            return null;
        }

        /// <summary>
        /// Reads from the stream and receives the next available frame
        /// from the specified component at the current stream position.
        /// This is a helper method for seeking logic.
        /// </summary>
        /// <param name="component">The component.</param>
        /// <returns>The next available frame.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MediaFrame StreamPositionDecode(MediaComponent component)
        {
            while (this.signalAbortReadsRequested.Value == false)
            {
                // We may have hit the end of our stream, but
                // we'll continue decoding (and therefore returning)
                // frames until the buffer is cleared.
                if (component.ReceiveNextFrame() is { } frame)
                {
                    return frame;
                }

                if (!this.IsAtEndOfStream)
                {
                    this.Read();
                }
                else
                {
                    return null;
                }
            }

            return null;
        }
    }
}
