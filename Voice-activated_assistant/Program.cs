/**
 * 語音助理啟動說明：
 * 1. 確保已安裝 .NET 10 Runtime / SDK。
 * 2. 在終端機執行 `dotnet run` 即可啟動。
 * 3. 程式啟動後會自動監測麥克風，每 10 秒進行一次語音轉文字。
 * 4. 按下 ESC 鍵可停止程式。
 */

// See https://aka.ms/new-console-template for more information
using Voice_activated_assistant;
using Whisper.net;
using Whisper.net.Ggml;
using System.Speech.Synthesis;

// 指定輸出為 UTF8
Console.OutputEncoding = System.Text.Encoding.UTF8;

string currentDirectory = Environment.CurrentDirectory;
Console.WriteLine($"目前的工作目錄: {currentDirectory}");

Console.WriteLine("\n請選擇使用的模型版本：");
Console.WriteLine("1. 官方最小模型 (Tiny, 約 31MB) - [自動下載]");
Console.WriteLine("2. 繁體中文微調模型 (Tiny-zh-TW, 約 74MB) - [需手動下載]");
Console.Write("請輸入選擇 (1 或 2，預設為 1): ");

string choice = Console.ReadLine() ?? "1";
string modelName;
if (choice == "2")
{
    modelName = "ggml-tiny-zh_tw.bin";
    if (!File.Exists(modelName))
    {
        Console.WriteLine($"\n❌ 找不到繁體中文模型檔案: {modelName}");
        Console.WriteLine("請至以下網址下載並放入程式目錄後重新執行：");
        Console.WriteLine("https://huggingface.co/xmzhu/whisper-tiny-zh-TW/resolve/main/ggml-tiny-zh_tw.bin");
        Console.WriteLine("\n按任意鍵結束...");
        Console.ReadKey();
        return;
    }
}
else
{
    modelName = "ggml-tiny-q5_1.bin";
    if (File.Exists(modelName))
    {
        Console.WriteLine($"✅ {modelName} 檔案已經存在，不須下載模型");
    }
    else
    {
        Console.WriteLine($"\n🈚 {modelName} 檔案不存在，準備從官方下載最小模型 (GgmlType.Tiny)");

        using var httpClient = new HttpClient();
        using var modelStream = await new WhisperGgmlDownloader(httpClient).GetGgmlModelAsync(GgmlType.Tiny, QuantizationType.Q5_1);
        using var fileWriter = File.OpenWrite(modelName);
        await modelStream.CopyToAsync(fileWriter);
        Console.WriteLine($"✅ {modelName} 下載完成！");
    }
}

Console.WriteLine($"\n🚀 正在啟動語音助理，使用模型: {modelName}");
using var whisperFactory = WhisperFactory.FromPath(modelName);
using var processor = whisperFactory.CreateBuilder()
    .WithLanguage("auto") // auto、zh-TW、zh-CN、zh
    .WithThreads(Environment.ProcessorCount) // 使用所有可用的執行緒以達到最高速度
    .Build();

var recorder = new AudioRecorder();
bool isRunning = true;

// 初始化 TTS
using var synth = new SpeechSynthesizer();
synth.SetOutputToDefaultAudioDevice();

string readyMsg = "程式準備完畢，請說話！";
Console.WriteLine($"\n✅ {readyMsg}\n");
synth.SpeakAsync(readyMsg); // 非同步播放，不卡住啟動流程

while (isRunning)
{
    Console.Write("\r🎙️  正在聽...".PadRight(30));
    recorder.StartRecording();
    
    // 動態等待：最長等待 15 秒，或者直到偵測到說話結束（靜音自動停止）
    int maxWaitMs = 15000;
    int waitedMs = 0;
    while (waitedMs < maxWaitMs)
    {
        if (recorder.ShouldStopDueToSilence()) 
        {
            Console.WriteLine("\n🛑 偵測到停頓，處理中...");
            break;
        }
        await Task.Delay(100);
        waitedMs += 100;
        
        if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape) 
        {
            isRunning = false;
            break;
        }
    }
    recorder.StopRecording();

    using var audioStream = recorder.GetAudioStream();
    if (audioStream != null && audioStream.Length > 0)
    {
        Console.WriteLine("\r⚙️  辨識中...".PadRight(30));
        await foreach (var result in processor.ProcessAsync(audioStream))
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} | {result.Start}->{result.End}: {result.Text}");
        }
    }
}

Console.WriteLine("✅ 程式已結束!");
Console.ReadLine();
