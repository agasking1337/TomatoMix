using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using TomatoMix.Models;

namespace TomatoMix.Services;

/// <summary>
/// Real-time 10-band Biquad DSP Audio Engine.
/// Can either:
/// 1) Play a rich built-in synth/pink-noise reference loop processed live through the 10-band EQ so you can immediately hear EQ changes without external drivers, OR
/// 2) Route and filter live WASAPI Loopback audio from any input device through the 10-band BiQuadFilter cascade to a selected output device.
/// </summary>
public class RealtimeEqAudioEngine : IDisposable
{
    private WasapiOut? _wasapiOut;
    private WasapiOut? _micMonitorOut;
    private WasapiCapture? _micCapture;
    private BufferedWaveProvider? _micBuffer;
    private EqSampleProvider? _eqSampleProvider;
    private EqSampleProvider? _micEqSampleProvider;
    private readonly object _lock = new();

    public bool IsAuditioningTestSound { get; private set; }
    public bool IsMicMonitoring { get; private set; }

    public void StartTestSoundAudition(double[] gainsDb, double preampDb, bool isEnabled)
    {
        Stop();
        lock (_lock)
        {
            try
            {
                var generator = new ReferenceSynthLoopProvider(48000);
                _eqSampleProvider = new EqSampleProvider(generator, gainsDb, preampDb, isEnabled);
                _wasapiOut = new WasapiOut(AudioClientShareMode.Shared, 40);
                _wasapiOut.Init(_eqSampleProvider);
                _wasapiOut.Play();
                IsAuditioningTestSound = true;
            }
            catch
            {
                Stop();
            }
        }
    }

    public void UpdateEqParameters(double[] gainsDb, double preampDb, bool isEnabled)
    {
        lock (_lock)
        {
            _eqSampleProvider?.UpdateFilters(gainsDb, preampDb, isEnabled);
        }
    }

    public void StartMicMonitor(MMDevice? captureDevice, double[] gainsDb, double preampDb, bool isEnabled)
    {
        StopMicMonitor();
        if (captureDevice == null) return;

        lock (_lock)
        {
            try
            {
                _micCapture = new WasapiCapture(captureDevice, true, 30);
                _micBuffer = new BufferedWaveProvider(_micCapture.WaveFormat)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(250)
                };

                _micCapture.DataAvailable += (_, e) =>
                {
                    if (e.BytesRecorded > 0)
                    {
                        _micBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
                    }
                };

                var sampleProvider = _micBuffer.ToSampleProvider();
                _micEqSampleProvider = new EqSampleProvider(sampleProvider, gainsDb, preampDb, isEnabled);
                _micMonitorOut = new WasapiOut(AudioClientShareMode.Shared, 35);
                _micMonitorOut.Init(_micEqSampleProvider);

                _micCapture.StartRecording();
                _micMonitorOut.Play();
                IsMicMonitoring = true;
            }
            catch
            {
                StopMicMonitor();
            }
        }
    }

    public void UpdateMicEqParameters(double[] gainsDb, double preampDb, bool isEnabled)
    {
        lock (_lock)
        {
            _micEqSampleProvider?.UpdateFilters(gainsDb, preampDb, isEnabled);
        }
    }

    public void StopMicMonitor()
    {
        lock (_lock)
        {
            IsMicMonitoring = false;
            try
            {
                _micCapture?.StopRecording();
                _micCapture?.Dispose();
            }
            catch
            {
            }
            _micCapture = null;

            try
            {
                _micMonitorOut?.Stop();
                _micMonitorOut?.Dispose();
            }
            catch
            {
            }
            _micMonitorOut = null;
            _micEqSampleProvider = null;
            _micBuffer = null;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            IsAuditioningTestSound = false;

            try
            {
                _wasapiOut?.Stop();
                _wasapiOut?.Dispose();
            }
            catch
            {
            }
            _wasapiOut = null;
            _eqSampleProvider = null;
        }
    }

    public void Dispose()
    {
        Stop();
        StopMicMonitor();
    }

    private sealed class EqSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly object _filterLock = new();
        private BiQuadFilter[] _leftFilters = new BiQuadFilter[10];
        private BiQuadFilter[] _rightFilters = new BiQuadFilter[10];
        private float _preampGainLinear = 1.0f;
        private bool _isEnabled = true;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public EqSampleProvider(ISampleProvider source, double[] gainsDb, double preampDb, bool isEnabled)
        {
            _source = source;
            UpdateFilters(gainsDb, preampDb, isEnabled);
        }

        public void UpdateFilters(double[] gainsDb, double preampDb, bool isEnabled)
        {
            lock (_filterLock)
            {
                _isEnabled = isEnabled;
                int sampleRate = _source.WaveFormat.SampleRate;

                // Calculate anti-clipping headroom + preamp
                double maxBoost = gainsDb.Length > 0 ? gainsDb.Max() : 0.0;
                double effectivePreampDb = preampDb;
                if (maxBoost > 2.0 && effectivePreampDb + maxBoost > 3.0)
                {
                    effectivePreampDb = Math.Min(effectivePreampDb, -(maxBoost * 0.45));
                }

                _preampGainLinear = (float)Math.Pow(10.0, effectivePreampDb / 20.0);

                for (int i = 0; i < 10; i++)
                {
                    float freq = (float)EqProfile.StandardFrequencies[i];
                    float gain = i < gainsDb.Length ? (float)gainsDb[i] : 0f;
                    _leftFilters[i] = BiQuadFilter.PeakingEQ(sampleRate, freq, 1.414f, gain);
                    _rightFilters[i] = BiQuadFilter.PeakingEQ(sampleRate, freq, 1.414f, gain);
                }
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            lock (_filterLock)
            {
                if (!_isEnabled)
                    return samplesRead;

                int channels = _source.WaveFormat.Channels;
                for (int n = 0; n < samplesRead; n++)
                {
                    int idx = offset + n;
                    float sample = buffer[idx] * _preampGainLinear;
                    bool isRight = channels >= 2 && (n % channels == 1);
                    var bank = isRight ? _rightFilters : _leftFilters;

                    for (int b = 0; b < 10; b++)
                    {
                        sample = bank[b].Transform(sample);
                    }

                    // Soft clip limiter to prevent harsh digital distortion
                    if (sample > 0.98f) sample = 0.98f;
                    else if (sample < -0.98f) sample = -0.98f;

                    buffer[idx] = sample;
                }
            }
            return samplesRead;
        }
    }

    /// <summary>
    /// Generates a warm full-spectrum musical chord + sub-bass pulse + hi-hat transient loop (31Hz to 16kHz)
    /// so the user can immediately hear sub-bass, mid-range footsteps/vocals, and air/treble bands when moving EQ sliders.
    /// </summary>
    private sealed class ReferenceSynthLoopProvider : ISampleProvider
    {
        private readonly int _sampleRate;
        private long _samplePos;
        private readonly Random _rng = new(42);
        private float _pink0, _pink1, _pink2;

        public WaveFormat WaveFormat { get; }

        public ReferenceSynthLoopProvider(int sampleRate)
        {
            _sampleRate = sampleRate;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Chord frequencies covering sub-bass (55Hz, 110Hz), mids (220Hz, 330Hz, 440Hz, 660Hz), and harmonics + crisp hi-hat transients
            double[] chordFreqs = [55.0, 110.0, 164.81, 220.0, 277.18, 329.63, 440.0, 659.25, 880.0, 1760.0, 3520.0];
            double[] chordAmps = [0.24, 0.20, 0.12, 0.14, 0.10, 0.10, 0.08, 0.06, 0.05, 0.03, 0.02];

            for (int i = 0; i < count; i += 2)
            {
                double t = (double)_samplePos / _sampleRate;
                _samplePos++;

                // Rhythmic kick / sub-bass envelope every 0.5s (120 BPM)
                double beatPhase = (t * 2.0) % 1.0;
                double kickEnv = Math.Exp(-beatPhase * 9.0);
                double kickFreq = 48.0 + 70.0 * Math.Exp(-beatPhase * 28.0);
                double kick = Math.Sin(2.0 * Math.PI * kickFreq * beatPhase) * kickEnv * 0.32;

                // Warm synth pad chord across the spectrum
                double pad = 0.0;
                for (int c = 0; c < chordFreqs.Length; c++)
                {
                    pad += Math.Sin(2.0 * Math.PI * chordFreqs[c] * t) * chordAmps[c];
                }

                // Hi-hat / footstep transient on off-beats + subtle pink noise bed (so 4kHz-16kHz bands are clearly audible)
                double hatPhase = ((t * 4.0) + 0.5) % 1.0;
                double hatEnv = Math.Exp(-hatPhase * 22.0);
                float white = (float)(_rng.NextDouble() * 2.0 - 1.0);
                _pink0 = 0.997f * _pink0 + white * 0.029f;
                _pink1 = 0.985f * _pink1 + white * 0.032f;
                _pink2 = 0.950f * _pink2 + white * 0.048f;
                float pink = _pink0 + _pink1 + _pink2 + white * 0.02f;

                double hatAndAir = (white * hatEnv * 0.14) + (pink * 0.10);

                float mono = (float)((kick + pad * 0.45 + hatAndAir) * 0.42);
                buffer[offset + i] = mono;
                if (i + 1 < count)
                {
                    buffer[offset + i + 1] = mono;
                }
            }

            return count;
        }
    }
}
