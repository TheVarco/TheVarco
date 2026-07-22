using UnityEngine;

namespace CaveBlockout
{
    public sealed class CaveValidationSummary : MonoBehaviour
    {
        public bool passed;
        public float routeLength;
        public float totalRise;
        public float minimumWidth;
        public float minimumHeight;
        public float maximumSlope;
        public float minimumTurnRadius;
        public float maximumReverseDrop;
        public int branchCount;
        public int triangleCount;
        public int boundaryLoopCount;
        public int nonManifoldEdgeCount;
        public int windingMismatchCount;
        public int degenerateTriangleCount;
        [TextArea(3, 12)] public string details;
    }
}
