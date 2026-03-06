using System;
using System.Collections.Generic;
using System.Text;

namespace SubReel
{

    public class CrashReport
    {
        public string Title { get; set; } = "";
        public string FullText { get; set; } = "";
        public string ShortMessage { get; set; } = "";
        public string Solution { get; set; } = "";
        public string Message { get; set; } = "";  

        public static CrashReport Simple(string message, string solution = "")
        {
            return new CrashReport
            {
                Title = message,
                ShortMessage = message,
                Solution = solution,
                FullText = message
            };
        }
    }
}
