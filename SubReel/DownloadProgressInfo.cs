using System;
using System.Collections.Generic;
using System.Text;

namespace SubReel
{
    public class DownloadProgressInfo
    {
        public string FileName { get; set; } = "";
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }

        public double Percent =>
            TotalBytes > 0
                ? (double)BytesReceived / TotalBytes * 100
                : 0;
    }
}
