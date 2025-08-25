using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Closed_loop_feedback_gui
{
    public static class pulseParameters
    {
        public static readonly Dictionary<int, ulong> blankDurations = new Dictionary<int, ulong>
        {
            // ROUND(1_000_000 / FREQ) - 80 - 80
            // I mean, this could be a function but for the proof of concept I want to specify the values myself
            { 150, 6506 },
            { 130, 7532 },
            { 110, 8930 },
            { 90, 10951 },
            { 70, 14125 },
            { 50, 19840 },
        };

        public static int defaultFrequency = 130;
    }
}
