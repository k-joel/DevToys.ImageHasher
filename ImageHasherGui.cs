using DevToys.Api;
using System.ComponentModel.Composition;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static DevToys.Api.GUI;
using ImgMath = DevToys.ImageHasher.ImageHasherMath;

namespace DevToys.ImageHasher
{
    [Export(typeof(IGuiTool))]
    [Name("ImageHasher")]
    [ToolDisplayInformation(
        IconFontName = "FluentSystemIcons",
        IconGlyph = '\uec24',
        GroupName = PredefinedCommonToolGroupNames.Graphic,
        ResourceManagerAssemblyIdentifier = nameof(ImageHasherResourceAssemblyIdentifier),
        ResourceManagerBaseName = "DevToys.ImageHasher.Resource",
        ShortDisplayTitleResourceName = nameof(Resource.ShortDisplayTitle),
        LongDisplayTitleResourceName = nameof(Resource.LongDisplayTitle),
        DescriptionResourceName = nameof(Resource.Description),
        AccessibleNameResourceName = nameof(Resource.AccessibleName),
        SearchKeywordsResourceName = nameof(Resource.SearchKeywords))]
    public sealed class ImageHasherGui : IGuiTool
    {
        private readonly IUIImageViewer _imageOutput = ImageViewer("imageOutput");
        private readonly IUIImageViewer _imagePreview = ImageViewer("imagePreview");
        private readonly IUIMultiLineTextInput _outputHex = MultiLineTextInput("outputHex");

        enum HashFunction
        {
            PHash,
            DHashH,
            DHashV,
            AHash,
            WHash,
            DCT
        }

        private static readonly int DefaultHashSize = 16;
        private static readonly int MaxHashSize = 128;
        private static readonly int OutputViewSize = 512;
        private static readonly HashFunction DefaultHashFunction = HashFunction.PHash;

        private int _currHashSize = DefaultHashSize;
        private HashFunction _currHashFunc = DefaultHashFunction;
        private Image<Rgba32>? _loadedImage = null;
        private SandboxedFileReader? _prevSelectedFile = null;
        private readonly Dictionary<(HashFunction, int), (Image<Rgba32>, string)> _outputImageCache = new();


        public UIToolView View => new(
            isScrollable: true,
            SplitGrid()
                .Vertical()
                .LeftPaneLength(new UIGridLength(2, UIGridUnitType.Fraction))
                .RightPaneLength(new UIGridLength(1, UIGridUnitType.Fraction))

                .WithLeftPaneChild(
                    Grid()
                        .RowLargeSpacing()
                        .Rows(
                            Auto,
                            new UIGridLength(1, UIGridUnitType.Fraction))

                        .ColumnLargeSpacing()
                        .Columns(
                            new UIGridLength(1, UIGridUnitType.Fraction),
                            new UIGridLength(2, UIGridUnitType.Fraction))

                        .Cells(
                            Cell(0, 0, 1, 1,
                                Stack()
                                    .Vertical()
                                    .LargeSpacing()
                                    .WithChildren(
                                        SelectDropDownList("hashFunction")
                                            .Title("Hash Function")
                                            .WithItems(
                                                Item("Perceptual Hash", HashFunction.PHash),
                                                Item("Difference Hash (Horizontal)", HashFunction.DHashH),
                                                Item("Difference Hash (Vertical)", HashFunction.DHashV),
                                                Item("Average Hash", HashFunction.AHash),
                                                Item("Wavelet Hash", HashFunction.WHash),
                                                Item("DCT Coefficients (Normalized)", HashFunction.DCT))
                                            .Select(0)
                                            .OnItemSelected(OnHashFunctionSelected),
                                        NumberInput("hashSize")
                                            .Title("Hash Size")
                                            .Step(8).Minimum(8).Maximum(MaxHashSize)
                                            .Value(DefaultHashSize)
                                            .HideCommandBar()
                                            .OnValueChanged(OnHashSizeChanged)
                                    )
                                ),
                            Cell(0, 1, 2, 1,
                                _outputHex
                                    .Title("Output Hex")
                                    .ReadOnly().AlwaysWrap()
                                ),
                            Cell(1, 0, 1, 1,
                                    _imageOutput
                                        .Title("Output Image")
                                        .AlignVertically(UIVerticalAlignment.Top)
                                )
                            )
                    )

                .WithRightPaneChild(
                    Grid()
                        .RowLargeSpacing()
                        .Rows(
                            Auto,
                            Auto,
                            new UIGridLength(1, UIGridUnitType.Fraction))

                        .Columns(
                            new UIGridLength(1, UIGridUnitType.Fraction))

                        .Cells(
                            Cell(0, 0, 1, 1,
                                FileSelector("imageInput")
                                    .CanSelectOneFile()
                                    .LimitFileTypesToImages()
                                    .OnFilesSelected(OnImageFileSelected)
                                //.WithFiles(DefaultFile)
                                ),
                            Cell(1, 0, 1, 1,
                                _imagePreview
                                    .Title("Preview Image")
                                    .AlignHorizontally(UIHorizontalAlignment.Stretch)
                                    .AlignVertically(UIVerticalAlignment.Top)
                                )


                        )
                   )
            );

        public void OnDataReceived(string dataTypeName, object? parsedData)
        {
            if (dataTypeName == PredefinedCommonDataTypeNames.Image && parsedData is Image<Rgba32> image)
            {
                SetInputImage(image);
            }
        }

        private async void OnImageFileSelected(SandboxedFileReader[] files)
        {
            if (files.Length == 0) return;

            var selectedFile = files[0];
            if (selectedFile.Equals(_prevSelectedFile)) return;

            _prevSelectedFile = selectedFile;

            // Update preview
            try
            {
                await using var stream = await selectedFile.GetNewAccessToFileContentAsync(CancellationToken.None);
                var loadedImage = await Image.LoadAsync<Rgba32>(stream);

                SetInputImage(loadedImage);
            }
            catch (Exception ex)
            {
                _imagePreview.Title("Error!");
                _imagePreview.Clear();
                _outputHex.Text($"Error Loading Input Image: {ex.Message}");
            }
        }

        private void OnHashSizeChanged(double obj)
        {
            _currHashSize = Math.Clamp((int)obj, 8, MaxHashSize);
            OnGenerateHashImage();
        }

        private void OnHashFunctionSelected(IUIDropDownListItem? obj)
        {
            _currHashFunc = obj?.Value is HashFunction hf ? hf : HashFunction.PHash;
            OnGenerateHashImage();
        }

        private void SetInputImage(Image<Rgba32> image)
        {
            _loadedImage?.Dispose();
            _loadedImage = image;

            _imagePreview.Title("Preview Image");
            _imagePreview.WithImage(_loadedImage, true);

            _outputImageCache.Clear();
            OnGenerateHashImage();
        }

        private async void OnGenerateHashImage()
        {
            if (_loadedImage is null) return;

            var hashSize = _currHashSize;
            var hashFunc = _currHashFunc;

            if (!_outputImageCache.TryGetValue((hashFunc, hashSize), out var cached))
            {
                try
                {
                    _imageOutput.Title("Calculating...");

                    cached = await Task.Run(() =>
                    {
                        using var resultImage = CalculateHash(_loadedImage, hashSize, hashFunc);

                        var outputImage = resultImage.Clone(ctx =>
                            ctx.Resize(OutputViewSize, OutputViewSize, KnownResamplers.NearestNeighbor));
                        var hex = ImgMath.ImageToHex(resultImage);

                        return (outputImage, hex);
                    });

                    _outputImageCache.Add((hashFunc, hashSize), cached);
                }
                catch (Exception ex)
                {
                    _imageOutput.Title("Error!");
                    _imageOutput.Clear();
                    _outputHex.Text($"Error Calculating Hash: {ex.Message}");
                    return;
                }
            }

            _imageOutput.Title($"Output Image ({hashFunc}, {hashSize}px)");
            _imageOutput.WithImage(cached.Item1, false);
            _outputHex.Text(cached.Item2);
        }

        private static Image<Rgba32> CalculateHash(Image<Rgba32> image, int hashSize, HashFunction hashFunc)
        {
            return hashFunc switch
            {
                HashFunction.PHash => ImgMath.CalculatePHash(image, hashSize),
                HashFunction.DHashH => ImgMath.CalculateDHashH(image, hashSize),
                HashFunction.DHashV => ImgMath.CalculateDHashV(image, hashSize),
                HashFunction.AHash => ImgMath.CalculateAHash(image, hashSize),
                HashFunction.WHash => ImgMath.CalculateWHash(image, hashSize),
                HashFunction.DCT => ImgMath.CalculateDCT(image, hashSize),
                _ => ImgMath.CalculatePHash(image, hashSize)
            };
        }
    }
}
