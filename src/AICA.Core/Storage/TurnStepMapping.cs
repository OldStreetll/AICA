using System;

namespace AICA.Core.Storage
{
    public class TurnStepMapping
    {
        public string SessionId { get; set; }
        public int TurnIndex { get; set; }
        public int[] StepIndices { get; set; } = Array.Empty<int>();

        public bool HasEdits => StepIndices.Length > 0;

        public static readonly TurnStepMapping Empty = new TurnStepMapping
        {
            TurnIndex = -1,
            StepIndices = Array.Empty<int>()
        };
    }
}
