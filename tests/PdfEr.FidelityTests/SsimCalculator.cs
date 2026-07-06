using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;

namespace PdfEr.FidelityTests;

public class SsimCalculator
{
    private const float C1 = 6.5025f;
    private const float C2 = 58.5225f;

    public static double CalculateSSIM(Image<Rgba32> img1, Image<Rgba32> img2)
    {
        if (img1.Width != img2.Width || img1.Height != img2.Height)
            throw new ArgumentException("Images must have same dimensions");

        var ssimMap = new double[img1.Height, img1.Width];
        var windowSize = 8;

        for (int y = 0; y <= img1.Height - windowSize; y += 4)
        {
            for (int x = 0; x <= img1.Width - windowSize; x += 4)
            {
                ssimMap[y, x] = ComputeWindowSSIM(img1, img2, x, y, windowSize);
            }
        }

        double mean = 0;
        int count = 0;
        foreach (var val in ssimMap)
        {
            if (val > 0)
            {
                mean += val;
                count++;
            }
        }

        return count > 0 ? mean / count : 0;
    }

    private static double ComputeWindowSSIM(Image<Rgba32> img1, Image<Rgba32> img2, int x, int y, int windowSize)
    {
        double sum1 = 0, sum2 = 0, sum1Sq = 0, sum2Sq = 0, sumProd = 0;
        int count = 0;

        for (int dy = 0; dy < windowSize && y + dy < img1.Height; dy++)
        {
            for (int dx = 0; dx < windowSize && x + dx < img1.Width; dx++)
            {
                var p1 = img1[x + dx, y + dy];
                var p2 = img2[x + dx, y + dy];

                float lum1 = GetLuminance(p1);
                float lum2 = GetLuminance(p2);

                sum1 += lum1;
                sum2 += lum2;
                sum1Sq += lum1 * lum1;
                sum2Sq += lum2 * lum2;
                sumProd += lum1 * lum2;
                count++;
            }
        }

        if (count == 0) return 0;

        double mean1 = sum1 / count;
        double mean2 = sum2 / count;
        double var1 = sum1Sq / count - mean1 * mean1;
        double var2 = sum2Sq / count - mean2 * mean2;
        double covar = sumProd / count - mean1 * mean2;

        double ssim = ((2 * mean1 * mean2 + C1) * (2 * covar + C2)) /
                      ((mean1 * mean1 + mean2 * mean2 + C1) * (var1 + var2 + C2));

        return Math.Max(0, Math.Min(1, ssim));
    }

    private static float GetLuminance(Rgba32 pixel) =>
        (0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B) / 255f;
}
