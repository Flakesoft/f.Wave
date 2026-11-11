// f.Wave build 3 - file 3 of 4: AudioAnalyzer.cs
using System;
using System.Diagnostics;
using NAudio.Wave;

namespace f.Wave
{
    /// <summary>
    /// Listens to whatever your PC is playing (WASAPI loopback), runs an FFT on it
    /// and turns the result into N bar levels between 0 and 1.
    /// </summary>
    public sealed class AudioAnalyzer : IDisposable
    {
        // =====================================================================
        //  KNOBS - change these numbers to tune how the visualizer feels.
        // =====================================================================

        // Lowest and highest frequency shown (Hz).
        private const float MinFrequency = 50f;
        private const float MaxFrequency = 16000f;

        // Loudness range (in dB) that maps to "empty bar" and "full bar".
        // Bars always too small?  Raise FloorDb (e.g. -65) or CeilingDb (e.g. -30).
        // Bars always maxed out?  Lower them (FloorDb -85, CeilingDb -15).
        private const float FloorDb = -75f;
        private const float CeilingDb = -20f;

        // Boosts highs / cuts bass so the bars look balanced. 0 = off, 3 = natural.
        private const float TiltDbPerOctave = 3f;

        // 1 = linear, higher = more contrast between quiet and loud.
        private const float Contrast = 1.4f;

        // How fast bars rise (0..1, higher = snappier) and fall (lower = floatier).
        private const float Attack = 0.6f;
        private const float Release = 0.12f;

        // =====================================================================
        //  Internals - no need to touch anything below this line.
        // =====================================================================

        private const int FftSize = 4096; // must be a power of two

        private readonly int _bandCount;
        private readonly object _lock = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly float[] _ring = new float[FftSize]; // last FftSize mono samples
        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];
        private readonly float[] _window = new float[FftSize];
        private readonly float[] _cos = new float[FftSize / 2];
        private readonly float[] _sin = new float[FftSize / 2];

        private readonly float[] _levels;
        private readonly int[] _binLo;
        private readonly int[] _binHi;
        private readonly float[] _tilt;

        private WasapiLoopbackCapture _capture;
        private int _sampleRate, _channels, _bits;
        private bool _isFloat;
        private int _write;
        private bool _hasData;
        private long _lastDataMs;

        /// <summary>Set when capture dies unexpectedly (device unplugged, etc.).</summary>
        public string LastError { get; private set; }

        public AudioAnalyzer(int bandCount)
        {
            _bandCount = bandCount;
            _levels = new float[bandCount];
            _binLo = new int[bandCount];
            _binHi = new int[bandCount];
            _tilt = new float[bandCount];

            for (int i = 0; i < FftSize; i++)
                _window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));

            for (int k = 0; k < FftSize / 2; k++)
            {
                double angle = -2.0 * Math.PI * k / FftSize;
                _cos[k] = (float)Math.Cos(angle);
                _sin[k] = (float)Math.Sin(angle);
            }
        }

        public void Start()
        {
            Stop();
            LastError = null;

            var capture = new WasapiLoopbackCapture();
            var format = capture.WaveFormat;

            bool isPcm = format.Encoding == WaveFormatEncoding.Pcm;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
            if (!isPcm && !isFloat)
            {
                capture.Dispose();
                throw new NotSupportedException("unsupported audio format (" + format.Encoding + ")");
            }

            switch (format.BitsPerSample)
            {
                case 8:
                case 16:
                case 24:
                case 32:
                    break;
                default:
                    capture.Dispose();
                    throw new NotSupportedException("unsupported audio format (" + format.BitsPerSample + " bit)");
            }

            _sampleRate = format.SampleRate;
            _channels = format.Channels;
            _bits = format.BitsPerSample;
            _isFloat = isFloat;
            BuildBands();

            lock (_lock)
            {
                Array.Clear(_ring, 0, _ring.Length);
                _write = 0;
                _hasData = false;
            }

            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnStopped;
            _capture = capture;
            capture.StartRecording();
        }

        public void Stop()
        {
            var capture = _capture;
            _capture = null;

            if (capture != null)
            {
                capture.DataAvailable -= OnData;
                capture.RecordingStopped -= OnStopped;
                try { capture.StopRecording(); } catch { }
                try { capture.Dispose(); } catch { }
            }

            lock (_lock) { _hasData = false; }
            Array.Clear(_levels, 0, _levels.Length);
        }

        public void Dispose() => Stop();

        // ---------------------------------------------------------------------
        //  Capture thread: just collect samples into the ring buffer.
        // ---------------------------------------------------------------------
        private void OnData(object sender, WaveInEventArgs e)
        {
            int bytesPerSample = _bits / 8;
            int frameSize = bytesPerSample * _channels;
            if (frameSize == 0) return;
            int frames = e.BytesRecorded / frameSize;

            lock (_lock)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    int offset = frame * frameSize;
                    float sum = 0f;

                    for (int c = 0; c < _channels; c++)
                    {
                        int o = offset + c * bytesPerSample;
                        sum += ReadSample(e.Buffer, o, _bits, _isFloat);
                    }

                    _ring[_write] = sum / _channels;
                    _write = (_write + 1) % FftSize;
                }

                _hasData = true;
                _lastDataMs = _clock.ElapsedMilliseconds;
            }
        }

        private static float ReadSample(byte[] buffer, int offset, int bits, bool isFloat)
        {
            if (isFloat && bits == 32)
                return BitConverter.ToSingle(buffer, offset);

            switch (bits)
            {
                case 8:
                    return ((sbyte)buffer[offset]) / 128f;
                case 16:
                    return BitConverter.ToInt16(buffer, offset) / 32768f;
                case 24:
                    return ReadInt24(buffer, offset) / 8388608f;
                case 32:
                    return BitConverter.ToInt32(buffer, offset) / 2147483648f;
                default:
                    return 0f;
            }
        }

        private static float ReadInt24(byte[] buffer, int offset)
        {
            int value = buffer[offset] |
                        (buffer[offset + 1] << 8) |
                        (buffer[offset + 2] << 16);

            if ((value & 0x00800000) != 0)
                value |= unchecked((int)0xFF000000);

            return value;
        }

        private void OnStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                LastError = e.Exception.Message;
        }

        // ---------------------------------------------------------------------
        //  UI thread, ~60 times per second: FFT -> bands -> smoothing.
        // ---------------------------------------------------------------------
        public float[] GetLevels()
        {
            bool silent;

            lock (_lock)
            {
                // Windows sends NO data while nothing is playing, so "no data
                // for 150 ms" means silence.
                silent = _capture == null || !_hasData || (_clock.ElapsedMilliseconds - _lastDataMs) > 150;

                if (!silent)
                {
                    // Copy the ring buffer oldest -> newest and apply the window.
                    for (int i = 0; i < FftSize; i++)
                    {
                        _re[i] = _ring[(_write + i) % FftSize] * _window[i];
                        _im[i] = 0f;
                    }
                }
            }

            if (!silent)
                Fft(_re, _im);

            for (int i = 0; i < _bandCount; i++)
            {
                float target = 0f;

                if (!silent)
                {
                    float sum = 0f;
                    int count = _binHi[i] - _binLo[i];
                    for (int b = _binLo[i]; b < _binHi[i]; b++)
                        sum += MathF.Sqrt(_re[b] * _re[b] + _im[b] * _im[b]);

                    // Divide by FftSize/4 so a full-scale sine wave = 1.0 (0 dB).
                    float magnitude = (sum / count) / (FftSize / 4f);
                    float db = 20f * MathF.Log10(magnitude + 1e-9f) + _tilt[i];

                    target = Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
                    target = MathF.Pow(target, Contrast);
                }

                float current = _levels[i];
                float speed = target > current ? Attack : Release;
                _levels[i] = current + (target - current) * speed;
            }

            return _levels;
        }

        // Works out which FFT bins belong to each bar (log-spaced, like hearing).
        private void BuildBands()
        {
            float maxF = Math.Min(MaxFrequency, _sampleRate * 0.45f);
            float ratio = maxF / MinFrequency;

            for (int i = 0; i < _bandCount; i++)
            {
                float fLo = MinFrequency * MathF.Pow(ratio, (float)i / _bandCount);
                float fHi = MinFrequency * MathF.Pow(ratio, (float)(i + 1) / _bandCount);

                int lo = (int)MathF.Floor(fLo * FftSize / _sampleRate);
                int hi = (int)MathF.Ceiling(fHi * FftSize / _sampleRate);

                lo = Math.Max(1, lo);
                hi = Math.Min(FftSize / 2 - 1, Math.Max(hi, lo + 1));

                _binLo[i] = lo;
                _binHi[i] = hi;
                _tilt[i] = TiltDbPerOctave * MathF.Log2(MathF.Sqrt(fLo * fHi) / 1000f);
            }
        }

        // Plain radix-2 FFT, in place.
        private void Fft(float[] re, float[] im)
        {
            int n = re.Length;

            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;

                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len / 2;
                int step = n / len;

                for (int i = 0; i < n; i += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        float wr = _cos[k * step];
                        float wi = _sin[k * step];

                        int a = i + k;
                        int b = a + half;

                        float tr = re[b] * wr - im[b] * wi;
                        float ti = re[b] * wi + im[b] * wr;

                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                    }
                }
            }
        }
    }
}
