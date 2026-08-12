using UnityEngine;

namespace CaveBlockout.Decor
{
    /// <summary>
    /// Links a spawned prop back to the <see cref="CaveDecorPlacement"/> that produced it.
    ///
    /// Name matching was the obvious cheaper option and it is what the old art-pass builder did
    /// (<c>child.name.Contains(fbxName.Replace(" ", ""))</c>), which broke as soon as Unity started
    /// appending " (1)" to duplicates. An explicit id survives renaming, reordering and re-parenting,
    /// which the eraser and the rebuild both depend on.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CaveDecorInstance : MonoBehaviour
    {
        public string placementId;
        public string paletteId;
        public string zoneId;
    }
}
