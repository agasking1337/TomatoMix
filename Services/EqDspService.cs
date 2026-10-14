using Avalonia;
using NAudio.Dsp;
using TomatoMix.Models;

namespace TomatoMix.Services;

public static class EqDspService
{
    private const double SampleRate = 48000.0;
    private const double DefaultQ = 1.414; // 1 octave bandwidth for 10-band graphic/parametric EQ

    /// <summary>
    /// Creates a 10-band cascade of NAudio BiQuadFilter peaking EQs for real-time audio sample processing.
    /// </summary>
    public static BiQuadFilter[] CreateFilterChain(double[] gainsDb)
    {
        var filters = new BiQuadFilter[10];
        for (int i = 0; i < 10; i++)
        {
            float freq = (float)EqProfile.StandardFrequencies[i];
            float gain = i < gainsDb.Length ? (float)gainsDb[i] : 0f;
            filters[i] = BiQuadFilter.PeakingEQ((float)SampleRate, freq, (float)DefaultQ, gain);
        }
        return filters;
    }

    /// <summary>
    /// Evaluates the combined dB magnitude response of the 10-band biquad cascade + preamp at any frequency f (Hz).
    /// </summary>
    public static double EvaluateFrequencyResponseDb(double freqHz, double[] gainsDb, double preampDb, bool isEnabled)
    {
        if (!isEnabled)
            return 0.0;

        double totalDb = preampDb;
        double omega = 2.0 * Math.PI * freqHz / SampleRate;
        double cosW = Math.Cos(omega);
        double cos2W = Math.Cos(2.0 * omega);
        double sinW = Math.Sin(omega);
        double sin2W = Math.Sin(2.0 * omega);

        for (int i = 0; i < 10; i++)
        {
            double gainDb = i < gainsDb.Length ? gainsDb[i] : 0.0;
            if (Math.Abs(gainDb) < 0.01)
                continue;

            double f0 = EqProfile.StandardFrequencies[i];
            double w0 = 2.0 * Math.PI * f0 / SampleRate;
            double alpha = Math.Sin(w0) / (2.0 * DefaultQ);
            double A = Math.Pow(10.0, gainDb / 40.0);
            double cosW0 = Math.Cos(w0);

            double b0 = 1.0 + alpha * A;
            double b1 = -2.0 * cosW0;
            double b2 = 1.0 - alpha * A;
            double a0 = 1.0 + alpha / A;
            double a1 = -2.0 * cosW0;
            double a2 = 1.0 - alpha / A;

            // Normalize
            b0 /= a0;
            b1 /= a0;
            b2 /= a0;
            a1 /= a0;
            a2 /= a0;

            double numReal = b0 + b1 * cosW + b2 * cos2W;
            double numImag = -(b1 * sinW + b2 * sin2W);
            double denReal = 1.0 + a1 * cosW + a2 * cos2W;
            double denImag = -(a1 * sinW + a2 * sin2W);

            double magSq = (numReal * numReal + numImag * numImag) / Math.Max(1e-12, denReal * denReal + denImag * denImag);
            totalDb += 10.0 * Math.Log10(Math.Max(1e-12, magSq));
        }

        return Math.Clamp(totalDb, -15.0, 15.0);
    }

    /// <summary>
    /// Generates SVG-like path geometry strings (Stroke line + Fill area) for the SteelSeries Sonar EQ graph canvas.
    /// </summary>
    public static (Points curvePoints, Points fillPoints) ComputeGraphPoints(
        double width,
        double height,
        double[] gainsDb,
        double preampDb,
        bool isEnabled,
        int numSamples = 96)
    {
        var curve = new Points();
        var fill = new Points();

        const double minFreq = 20.0;
        const double maxFreq = 20000.0;
        double logMin = Math.Log10(minFreq);
        double logMax = Math.Log10(maxFreq);

        double midY = height / 2.0;
        fill.Add(new Point(0, midY));

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / (numSamples - 1);
            double x = t * width;
            double logF = logMin + t * (logMax - logMin);
            double freq = Math.Pow(10.0, logF);

            double db = EvaluateFrequencyResponseDb(freq, gainsDb, preampDb, isEnabled);
            // Map +12dB -> top (padding 10px), -12dB -> bottom (height - 10px)
            double usableHalf = (height - 20.0) / 2.0;
            double y = midY - (db / 12.0) * usableHalf;
            y = Math.Clamp(y, 4.0, height - 4.0);

            var pt = new Point(Math.Round(x, 1), Math.Round(y, 1));
            curve.Add(pt);
            fill.Add(pt);
        }

        fill.Add(new Point(width, midY));
        return (curve, fill);
    }

    /// <summary>
    /// Computes an effective loudness &amp; anti-clipping trim multiplier from the channel's EQ curve and Preamp.
    /// </summary>
    public static double ComputeEffectiveEqTrimScalar(double[] gainsDb, double preampDb, bool isEnabled)
    {
        if (!isEnabled)
            return 1.0;

        // Weight mid/bass bands to compute perceived loudness change + preamp gain, with anti-clipping headroom
        double[] weights = [0.06, 0.10, 0.12, 0.12, 0.14, 0.14, 0.12, 0.10, 0.06, 0.04];
        double weightedDb = 0.0;
        for (int i = 0; i < 10 && i < gainsDb.Length; i++)
        {
            weightedDb += gainsDb[i] * weights[i];
        }

        double totalDb = Math.Clamp(preampDb + weightedDb * 0.45, -12.0, 6.0);
        return Math.Clamp(Math.Pow(10.0, totalDb / 20.0), 0.25, 1.0);
    }
}
