using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DevToys.ImageHasher
{
    internal class ImageHasherMath
    {
        public static double ToGray(Rgba32 pixel)
        {
            return 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;
        }

        public static double[] LoadGrayscaleImage(Image<Rgba32> image)
        {
            int width = image.Width;
            int height = image.Height;
            double[] pixels = new double[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = image[x, y];
                    pixels[y * width + x] = ToGray(pixel);
                }
            }
            return pixels;
        }

        public static string ImageToHex(Image<Rgba32> image)
        {
            var sb = new System.Text.StringBuilder();

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var pixel = image[x, y];
                    // Convert to binary (1 if white/255, 0 if black/0)
                    sb.Append(pixel.R > 127 ? '1' : '0');
                }
            }

            // Convert binary string to hex
            string binary = sb.ToString();
            var hexBuilder = new System.Text.StringBuilder();

            for (int i = 0; i < binary.Length; i += 4)
            {
                int remaining = Math.Min(4, binary.Length - i);
                string chunk = binary.Substring(i, remaining).PadRight(4, '0');
                int value = Convert.ToInt32(chunk, 2);
                hexBuilder.Append(value.ToString("X"));
            }

            return hexBuilder.ToString();
        }

        public static Image<Rgba32> CombineImageOR(Image<Rgba32> image1, Image<Rgba32> image2)
        {
            int width = Math.Min(image1.Width, image2.Width);
            int height = Math.Min(image1.Height, image2.Height);
            var result = new Image<Rgba32>(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var p1 = image1[x, y];
                    var p2 = image2[x, y];
                    result[x, y] = new Rgba32(
                        (byte)(p1.R | p2.R),
                        (byte)(p1.G | p2.G),
                        (byte)(p1.B | p2.B),
                        255
                    );
                }
            }

            return result;
        }

        public static Image<Rgba32> CombineImageXOR(Image<Rgba32> image1, Image<Rgba32> image2)
        {
            int width = Math.Min(image1.Width, image2.Width);
            int height = Math.Min(image1.Height, image2.Height);
            var result = new Image<Rgba32>(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var p1 = image1[x, y];
                    var p2 = image2[x, y];
                    result[x, y] = new Rgba32(
                        (byte)(p1.R ^ p2.R),
                        (byte)(p1.G ^ p2.G),
                        (byte)(p1.B ^ p2.B),
                        255
                    );
                }
            }

            return result;
        }

        public static Image<Rgba32> CalculatePHash(Image<Rgba32> image, int hashSize = 16)
        {
            using var resized = image.Clone(ctx => ctx.Resize(hashSize, hashSize));
            var result = new Image<Rgba32>(hashSize, hashSize);

            double[] pixels = LoadGrayscaleImage(resized);

            double[] dct = ApplyDCT(pixels, hashSize);
            double avg = dct.Take(64).Average();

            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    int idx = y * hashSize + x;
                    byte color = (byte)(dct[idx] > avg ? 255 : 0);
                    result[x, y] = new Rgba32(color, color, color, 255);
                }
            }

            return result;
        }

        public static Image<Rgba32> CalculateDHashH(Image<Rgba32> image, int hashSize = 16)
        {
            using var resized = image.Clone(ctx => ctx.Resize(hashSize + 1, hashSize));
            var result = new Image<Rgba32>(hashSize, hashSize);

            double[] pixels = LoadGrayscaleImage(resized);

            int width = resized.Width;
            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    double gray1 = pixels[y * width + x];
                    double gray2 = pixels[y * width + (x + 1)];

                    byte color = (byte)(gray1 < gray2 ? 255 : 0);
                    result[x, y] = new Rgba32(color, color, color, 255);
                }
            }

            return result;
        }

        public static Image<Rgba32> CalculateDHashV(Image<Rgba32> image, int hashSize = 16)
        {
            using var resized = image.Clone(ctx => ctx.Resize(hashSize, hashSize + 1));
            var result = new Image<Rgba32>(hashSize, hashSize);

            double[] pixels = LoadGrayscaleImage(resized);

            int width = resized.Width;
            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    double gray1 = pixels[y * width + x];
                    double gray2 = pixels[(y + 1) * width + x];

                    byte color = (byte)(gray1 < gray2 ? 255 : 0);
                    result[x, y] = new Rgba32(color, color, color, 255);
                }
            }

            return result;
        }

        public static Image<Rgba32> CalculateDHashOR(Image<Rgba32> image, int hashSize = 16)
        {
            using var imageH = CalculateDHashH(image, hashSize);
            using var imageV = CalculateDHashV(image, hashSize);
            return CombineImageOR(imageH, imageV);
        }

        public static Image<Rgba32> CalculateDHashXOR(Image<Rgba32> image, int hashSize = 16)
        {
            using var imageH = CalculateDHashH(image, hashSize);
            using var imageV = CalculateDHashV(image, hashSize);
            return CombineImageXOR(imageH, imageV);
        }

        public static Image<Rgba32> CalculateAHash(Image<Rgba32> image, int hashSize = 16)
        {
            using var resized = image.Clone(ctx => ctx.Resize(hashSize, hashSize));
            var result = new Image<Rgba32>(hashSize, hashSize);

            double sum = 0;
            double[] pixels = LoadGrayscaleImage(resized);

            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    sum += pixels[y * hashSize + x];
                }
            }

            double avg = sum / (hashSize * hashSize);

            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    byte color = (byte)(pixels[y * hashSize + x] > avg ? 255 : 0);
                    result[x, y] = new Rgba32(color, color, color, 255);
                }
            }

            return result;
        }

        public static Image<Rgba32> CalculateWHash(Image<Rgba32> image, int hashSize = 16)
        {
            using var resized = image.Clone(ctx => ctx.Resize(hashSize, hashSize));
            var result = new Image<Rgba32>(hashSize, hashSize);

            double[] pixels = LoadGrayscaleImage(resized);

            double[] wavelet = ApplyHaarWavelet(pixels, hashSize);
            double avg = wavelet.Average();

            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    int idx = y * hashSize + x;
                    byte color = (byte)(wavelet[idx] > avg ? 255 : 0);
                    result[x, y] = new Rgba32(color, color, color, 255);
                }
            }

            return result;
        }

        public static Image<Rgba32> CalculateDCT(Image<Rgba32> image, int hashSize = 16)
        {
            using var resized = image.Clone(ctx => ctx.Resize(hashSize, hashSize));
            var result = new Image<Rgba32>(hashSize, hashSize);

            double[] pixels = LoadGrayscaleImage(resized);

            double[] dct = ApplyDCT(pixels, hashSize);

            // Find min and max for normalization
            double min = dct.Min();
            double max = dct.Max();
            double range = max - min;

            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    int idx = y * hashSize + x;
                    byte color = range > 0 ? (byte)((dct[idx] - min) / range * 255) : (byte)0;
                    result[x, y] = new Rgba32(color, color, color, 255);
                }
            }

            return result;
        }

        public static double[] ApplyDCT(double[] input, int size)
        {
            double[] output = new double[size * size];
            for (int v = 0; v < size; v++)
            {
                for (int u = 0; u < size; u++)
                {
                    double sum = 0;
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            sum += input[y * size + x] *
                                Math.Cos((2 * x + 1) * u * Math.PI / (2 * size)) *
                                Math.Cos((2 * y + 1) * v * Math.PI / (2 * size));
                        }
                    }
                    output[v * size + u] = sum;
                }
            }
            return output;
        }

        public static double[] ApplyHaarWavelet(double[] input, int size)
        {
            double[] output = new double[size * size];
            Array.Copy(input, output, input.Length);

            int currentSize = size;
            while (currentSize > 1)
            {
                // Apply to rows
                for (int y = 0; y < currentSize; y++)
                {
                    for (int x = 0; x < currentSize / 2; x++)
                    {
                        double a = output[y * size + 2 * x];
                        double b = output[y * size + 2 * x + 1];
                        output[y * size + x] = (a + b) / 2;
                        output[y * size + currentSize / 2 + x] = (a - b) / 2;
                    }
                }

                // Apply to columns
                for (int x = 0; x < currentSize; x++)
                {
                    for (int y = 0; y < currentSize / 2; y++)
                    {
                        double a = output[2 * y * size + x];
                        double b = output[(2 * y + 1) * size + x];
                        output[y * size + x] = (a + b) / 2;
                        output[(currentSize / 2 + y) * size + x] = (a - b) / 2;
                    }
                }

                currentSize /= 2;
            }

            return output;
        }
    }
}
