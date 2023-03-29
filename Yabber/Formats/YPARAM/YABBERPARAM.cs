using System.Collections.Generic;
using SoulsFormats;

namespace Yabber
{
    /// <summary>
    /// PARAM class override which does some extra things in the constructor.
    /// SF PARAM isn't fully supportive of just making new ones from scratch. This helps.
    /// </summary>
    public class YABBERPARAM : PARAM {
        /// <summary>
        /// Automatically determined based on spacing of row offsets; -1 if param had no rows.
        /// </summary>
        private new long DetectedSize { get; set; }

        public YABBERPARAM()
        {
            // New PARAMs need to actually start with DetectedSize -1 since they have no rows...
            DetectedSize = -1;
            Rows = new List<Row>();
        }
    }
}