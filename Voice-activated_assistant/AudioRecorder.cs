using NAudio.Wave;

namespace Voice_activated_assistant
{
    /// <summary>
    /// 使用 NAudio 錄音
    /// </summary>
    public class AudioRecorder : IDisposable
    {
        private WaveInEvent? waveSource = null;
        private WaveFileWriter? waveFile = null;
        private readonly MemoryStream memoryStream = new MemoryStream();
        private bool isRecording = false;
        private bool isSpeaking = false;
        private readonly float threshold = 0.008f; // 稍微調高一些，過濾更微弱的環境底噪
        private DateTime lastVoiceTime = DateTime.MinValue;
        private readonly int silenceDurationMs = 1500;

        // 預錄緩衝區：保存觸發前約 400ms 的音訊 (確保起手字完整)
        private readonly List<byte[]> preRollBuffer = new List<byte[]>();
        private readonly int maxPreRollBlocks = 20; 

        public AudioRecorder()
        {
            // 初始化錄音設備並保持長駐，避免重覆建立
            waveSource = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1)
            };
            waveSource.DataAvailable += WaveSource_DataAvailable;
            waveSource.StartRecording();
        }

        public void StartRecording()
        {
            // 重置記憶體流而不重新分配空間
            lock (memoryStream)
            {
                memoryStream.SetLength(0);
                memoryStream.Position = 0;
            }
            isRecording = true;
            isSpeaking = false;
            lastVoiceTime = DateTime.MinValue;
            preRollBuffer.Clear();
        }

        private void WaveSource_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!isRecording) return;

            float amplitude = 0;
            for (int index = 0; index < e.BytesRecorded; index += 2) // 全量掃描以提高精準度
            {
                short sample = BitConverter.ToInt16(e.Buffer, index);
                amplitude += Math.Abs(sample / 32768f);
            }
            amplitude /= (e.BytesRecorded / 2);

            // 如果有人正在說話，可以取消註解下一行來觀察音控數值
            // Console.Write($"\r音量: {amplitude:F4} ".PadRight(20));

            if (amplitude > threshold)
            {
                lastVoiceTime = DateTime.Now;
                if (!isSpeaking)
                {
                    isSpeaking = true;
                    Console.WriteLine("\n🎤 偵測到聲音...");
                    
                    lock (memoryStream)
                    {
                        if (waveFile == null)
                        {
                            waveFile = new WaveFileWriter(new IgnoreDisposeStream(memoryStream), waveSource!.WaveFormat);
                            // 寫入預錄段，確保開頭完整
                            foreach (var block in preRollBuffer)
                            {
                                waveFile.Write(block, 0, block.Length);
                            }
                            preRollBuffer.Clear();
                        }
                    }
                }

                lock (memoryStream)
                {
                    waveFile?.Write(e.Buffer, 0, e.BytesRecorded);
                }
            }
            else
            {
                if (isSpeaking)
                {
                    // 說話中但暫時短暫低於門檻，持續錄音
                    lock (memoryStream) { waveFile?.Write(e.Buffer, 0, e.BytesRecorded); }
                }
                else
                {
                    // 尚未觸發說話，將當前段存入預錄衝緩
                    preRollBuffer.Add(e.Buffer.ToArray());
                    if (preRollBuffer.Count > maxPreRollBlocks) preRollBuffer.RemoveAt(0);
                }
            }
        }

        public void StopRecording()
        {
            isRecording = false;
            isSpeaking = false;
            lock (memoryStream)
            {
                waveFile?.Dispose();
                waveFile = null;
            }
        }

        public Stream? GetAudioStream()
        {
            lock (memoryStream)
            {
                if (memoryStream.Length < 1000) return null;
                // Console.WriteLine($"📦 準備辨識音訊流 (大小: {memoryStream.Length / 1024.0:F2} KB)"); // Removed as per instruction
                return new MemoryStream(memoryStream.ToArray());
            }
        }

        public bool IsRecording() => isRecording;
        // public bool IsSpeaking() => isSpeaking; // Removed as per instruction
        public bool ShouldStopDueToSilence() => isRecording && isSpeaking && (DateTime.Now - lastVoiceTime).TotalMilliseconds > silenceDurationMs;

        public void Dispose()
        {
            waveSource?.StopRecording();
            waveSource?.Dispose();
            waveFile?.Dispose();
            memoryStream.Dispose();
        }

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
