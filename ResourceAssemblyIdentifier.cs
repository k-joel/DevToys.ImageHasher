using System.ComponentModel.Composition;
using DevToys.Api;

namespace DevToys.ImageHasher
{
    [Export(typeof(IResourceAssemblyIdentifier))]
    [Name(nameof(ImageHasherResourceAssemblyIdentifier))]
    internal sealed class ImageHasherResourceAssemblyIdentifier : IResourceAssemblyIdentifier
    {
        public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        {
            return ValueTask.FromResult(Array.Empty<FontDefinition>());
        }
    }
}
