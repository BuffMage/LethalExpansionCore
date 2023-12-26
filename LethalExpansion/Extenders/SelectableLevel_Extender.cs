﻿using System.Runtime.CompilerServices;

namespace LethalExpansion.Patches
{
    public static class SelectableLevel_Extender
    {
        private static readonly ConditionalWeakTable<SelectableLevel, SelectableLevelExtention> extention = new ConditionalWeakTable<SelectableLevel, SelectableLevelExtention>();

        public static void SetFireExitAmountOverwrite(this SelectableLevel level, int value)
        {
            if (!extention.TryGetValue(level, out var data))
            {
                data = new SelectableLevelExtention();
                extention.Add(level, data);
            }

            data.FireExitAmountOverwrite = value;
        }

        public static int GetFireExitAmountOverwrite(this SelectableLevel level)
        {
            if (extention.TryGetValue(level, out var data))
            {
                return data.FireExitAmountOverwrite;
            }

            return 0;
        }

        private class SelectableLevelExtention
        {
            public int FireExitAmountOverwrite { get; set; }
        }
    }
}
