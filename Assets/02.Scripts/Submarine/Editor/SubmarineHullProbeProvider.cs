using System;
using CaveBlockout;
using CaveBlockout.Editor;
using UnityEditor;
using UnityEngine;

namespace Varco.Submarine.EditorTools
{
    /// <summary>
    /// Hands the cave clearance validator the real submarine's shape.
    ///
    /// This lives in Assembly-CSharp-Editor rather than next to the validator because
    /// CaveBlockout.Editor is an assembly definition, and an assembly definition cannot reference the
    /// predefined assemblies - so it can never see SubmarineController. The predefined editor assembly
    /// can see both sides, which makes it the only place this wiring can exist.
    ///
    /// Why bother instead of copying four floats into the validator: the validator already had those
    /// four floats copied in, as a 3 x 3 x 6 m box taken from MAP_GUIDE.md's reference submarine. The
    /// prefab's capsule had since grown to 8.11 m long with its centre 3 m aft, and nothing connected
    /// the two, so the clearance gate passed a submarine that does not exist while the real one wedged
    /// itself at the Z2 -> Z3 throat. Reading the prefab means the gate cannot drift again.
    /// </summary>
    [InitializeOnLoad]
    public static class SubmarineHullProbeProvider
    {
        public const string SubmarinePrefabPath = "Assets/03.Prefabs/Submarine/Submarine_final.prefab";

        static SubmarineHullProbeProvider()
        {
            CaveClearanceValidator.HullProvider = Resolve;
            CaveClearanceValidator.IgnoreCollider = IsGameplayActor;
        }

        /// <summary>
        /// True for things the clearance sweep must not mistake for cave wall.
        ///
        /// The play scene puts the submarine, 52 pickups, three sharks and three whirlpools on the same
        /// layers as the rock, so a sweep that counts them measures where a shark was parked rather than
        /// whether the tunnel fits. MainMap holds none of them and passed on identical geometry while
        /// MainScene_final failed on Z6_Shark_3 at 555.9 m - the same cave, two answers.
        ///
        /// Named roots rather than component types because the item system identifies its own group the
        /// same way (CaveItemSpawner.ItemRoot), and because this has to recognise a shark without the cave
        /// assembly knowing what a shark is.
        /// </summary>
        private static bool IsGameplayActor(Collider candidate)
        {
            if (candidate == null)
                return true;

            if (candidate.GetComponentInParent<SubmarineController>() != null)
                return true;

            for (Transform node = candidate.transform; node != null; node = node.parent)
            {
                if (node.name == ItemGroupName || node.name == PlayerGroupName)
                    return true;
            }

            return false;
        }

        private const string ItemGroupName = "Items";
        private const string PlayerGroupName = "Players";

        /// <summary>
        /// Reads the movement capsule off the prefab asset.
        ///
        /// The prefab, not a scene instance: MainMap - the scene the blockout tools open - has no
        /// submarine in it at all (the submarine and players live only in MainScene_final). The scene
        /// instance in MainScene_final overrides position, rotation and name but not scale, so the
        /// prefab's own scale is authoritative.
        /// </summary>
        public static CaveHullProbe Resolve()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SubmarinePrefabPath);
            if (prefab == null)
                throw new InvalidOperationException(
                    $"The submarine prefab was not found at {SubmarinePrefabPath}. The cave clearance " +
                    "validator has no shape to test and will not guess one.");

            SubmarineController submarine = prefab.GetComponent<SubmarineController>();
            if (submarine == null)
                throw new InvalidOperationException(
                    $"{SubmarinePrefabPath} has no SubmarineController, so its hull capsule cannot be read.");

            return submarine.BuildHullProbe(prefab.transform.lossyScale);
        }

        /// <summary>
        /// Prints the resolved hull next to the proxy the validator used to assume, so the difference is
        /// on the record rather than in a commit message.
        /// </summary>
        [MenuItem("Tools/Underwater Cave/Report Submarine Hull Probe")]
        public static void ReportHullProbe()
        {
            CaveHullProbe hull = Resolve();
            Debug.Log(
                $"SUBMARINE_HULL_PROBE {hull}\n" +
                $"  width/height = {hull.radius * 2f:F2} m, length = {hull.height:F2} m\n" +
                $"  pivot to tail = {hull.AftReach:F2} m (a yaw of A degrees sweeps the tail " +
                $"{hull.AftReach:F2} * sin(A) sideways)\n" +
                "  retired proxy = 3.00 x 3.00 x 6.00 m box, centred on the centreline, pitched along " +
                "the climb");
        }
    }
}
