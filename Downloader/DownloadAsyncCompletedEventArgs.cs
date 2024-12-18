using System;
using System.Collections.Generic;
using System.Text;

namespace Downloader
{
    public class DownloadAsyncCompletedEventArgs : System.ComponentModel.AsyncCompletedEventArgs
    {
        public DownloadAsyncCompletedEventArgs(int taskId, Exception error, bool cancelled, object userState) : base(error, cancelled, userState)
        {
            TaskId = taskId;
        }

        public int TaskId { get; }
    }
}
