using NAudio.Wave;

namespace Voice_activated_assistant
{
    /// <summary>
    /// 使用 NAudio 錄音
    /// </summary>
    public class AudioRecorder
    {
        private WaveInEvent? waveSource = null;
        private WaveFileWriter? waveFile = null;
        private MemoryStream? memoryStream = null;
        private bool isRecording = false;
        private readonly float threshold = 0.005f; // 調低閾值，確保更容易偵測到聲音
        private DateTime lastVoiceTime = DateTime.MinValue;
        private readonly int silenceDurationMs = 1500; // 連續 1.5 秒沒聲音就停止錄音
        private bool isSpeaking = false;

        public void StartRecording()
        {
            memoryStream = new MemoryStream();
            waveSource = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1)
            };

            waveSource.DataAvailable += new EventHandler<WaveInEventArgs>(WaveSource_DataAvailable);
            waveSource.RecordingStopped += new EventHandler<StoppedEventArgs>(WaveSource_RecordingStopped);

            isSpeaking = false;
            waveSource.StartRecording();
        }

        private void WaveSource_DataAvailable(object? sender, WaveInEventArgs e)
        {
            float amplitude = 0;
            for (int index = 0; index < e.BytesRecorded; index += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, index);
                amplitude += Math.Abs(sample / 32768f);
            }
            amplitude /= (e.BytesRecorded / 2);

            // 如果有人正在說話，可以取消註解下一行來觀察音控數值
            // Console.Write($"\r音量: {amplitude:F4} ".PadRight(20));

            // 判斷當前是否有聲音
            if (amplitude > threshold)
            {
                lastVoiceTime = DateTime.Now;
                if (!isSpeaking)
                {
                    isSpeaking = true;
                    Console.WriteLine("\n🎤 偵測到聲音，開始錄製...");
                }

                if (!isRecording && memoryStream != null)
                {
                    waveFile = new WaveFileWriter(new IgnoreDisposeStream(memoryStream), waveSource!.WaveFormat);
                    isRecording = true;
                }
            }

            // 如果正在錄音，寫入數據
            if (isRecording)
            {
                waveFile?.Write(e.Buffer, 0, e.BytesRecorded);
                waveFile?.Flush();

                // 檢查是否超過靜音時間
                if (isSpeaking && (DateTime.Now - lastVoiceTime).TotalMilliseconds > silenceDurationMs)
                {
                    // 不要在此處對自己調用 StopRecording() 避免阻塞回呼執行緒
                    // 我們透過讓 StopRecording 被外部調用或標記狀態來處理
                    isSpeaking = false; 
                }
            }
        }

        private void WaveSource_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            waveFile?.Dispose();
            waveFile = null;
            waveSource?.Dispose();
            waveSource = null;
            isRecording = false;
            isSpeaking = false;
        }

        public void StopRecording()
        {
            if (waveSource != null)
            {
                waveSource.StopRecording();
                // 等待錄音真正停止 (DataAvailable 不再進來且 File 已 Dispose)
                int timeout = 0;
                while (isRecording && timeout < 20)
                {
                    Thread.Sleep(50);
                    timeout++;
                }
            }
        }

        public Stream? GetAudioStream()
        {
            if (memoryStream == null) return null;
            if (memoryStream.Length < 1000) // 1000 bytes 左右大約才不到 0.1 秒的音訊
            {
                return null;
            }
            Console.WriteLine($"📦 準備辨識音訊流 (大小: {memoryStream.Length / 1024.0:F2} KB)");
            memoryStream.Position = 0;
            return memoryStream;
        }

        public bool IsRecording() => isRecording;
        public bool IsSpeaking() => isSpeaking; 
        public bool ShouldStopDueToSilence() => isRecording && isSpeaking == false && (DateTime.Now - lastVoiceTime).TotalMilliseconds > silenceDurationMs;


        private class IgnoreDisposeStream : Stream
        {
            private readonly Stream _inner;
            public IgnoreDisposeStream(Stream inner) => _inner = inner;
            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
            protected override void Dispose(bool disposing) { }
        }
    }
}
