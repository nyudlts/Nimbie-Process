using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DiscUtils.Iso9660;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Nimbie_Rename_UI
{

    public static class AppInfo
    {
        public const string Version = "0.1.1-beta";
        public const string Title = $"Nimbie Process [v{Version}]";
    }

    public partial class MainWindow : Window
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 2;
        bool testMode;
        string? imageDirectory;
        string? updateDirectory;
        string? manifestFile;
        List<string>? filenames;
        List<string> metaFormats = new List<string>() {"audio", "video", "data" };
        Dictionary<string, string> imageFormats = new Dictionary<string, string>() { };
        StreamWriter? LogWriter;
        StreamWriter? UpdateLogWriter;
        int pendingLogWrites;
        TaskCompletionSource<bool>? logDrainTcs;
        readonly object logDrainLock = new object();
        const string Version = AppInfo.Version;
        const string RenameLogFileName = "nimbie-rename.log";
        const string UpdateLogFileName = "nimbie-update.log";
        string? LogPath;
        TextBox? OutputBox;
        string? OriginalDir;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void ShowAbout(Object? sender, RoutedEventArgs e)
        {
            return;
        }

        private async void PickImageFolder(object? sender, RoutedEventArgs e)
        {
            if (StorageProvider is null)
            {
                RenameOutputBox.Text += "StorageProvider is not available.";
                return;
            }

            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false
            });

            if (folder != null && folder.Count > 0) 
            {
                RenamePathBox.Text = folder[0].Path.LocalPath;
                var logPath = Path.Combine(RenamePathBox.Text!, RenameLogFileName); 
                //start the logWriter 
                SetLog(sender, e, logPath, RenameOutputBox);
                logVersion(sender, e, logPath, "rename");
            } else
            {
                //there needs to be an error here
            }

        }

        private async void SetLog(object? sender, RoutedEventArgs e, string logPath, TextBox? outputBox)
        {
            LogPath = logPath;
            LogWriter = new StreamWriter(logPath, append: false);
            OutputBox = outputBox;
        }

        private async void logVersion(object? sender, RoutedEventArgs e, string logLocation, string service)
        {
            Log($"Nimbie Process, v{Version}");
            Log("Writing log to " + logLocation);
            Log($"Running {service} process");
        }

        internal void Log(string message)
        {
            Interlocked.Increment(ref pendingLogWrites);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    OutputBox!.Text += $"{DateTime.Now:HH:mm:ss}  {message}\n";
                    OutputBox.CaretIndex = OutputBox.Text.Length;

                    LogWriter?.Write($"{DateTime.Now:HH:mm:ss}  {message}\n");
                    LogWriter?.Flush();
                }
                finally
                {
                    if (Interlocked.Decrement(ref pendingLogWrites) == 0)
                    {
                        lock (logDrainLock)
                        {
                            logDrainTcs?.TrySetResult(true);
                        }
                    }
                }
            });
        }

        private async Task CloseLogAsync()
        {
            Task waitForDrain;
            lock (logDrainLock)
            {
                if (Volatile.Read(ref pendingLogWrites) == 0)
                {
                    waitForDrain = Task.CompletedTask;
                }
                else
                {
                    logDrainTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    waitForDrain = logDrainTcs.Task;
                }
            }

            await waitForDrain;

            LogWriter?.Close();
            LogWriter = null;
        }

        private async void RenameFiles(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RenamePathBox.Text))
            {
                return;
            }
            imageDirectory = RenamePathBox.Text;

            if (string.IsNullOrWhiteSpace(imageDirectory))
            {
                return;
            }

            testMode = false;

            await Task.Run(() =>
            {

                // Check if the image directory exists
                if (!Directory.Exists(imageDirectory))
                {
                    Log("Invalid paths.");
                    return;
                }

                // Check if the manifest file exists
                if (!FindManifest())
                {
                    return;
                }

                //check that the manifest file is valid
                if (!ValidateManifest())
                {
                    Log("Manifest validation failed.");
                    return;
                }


                RemoveArtifacts();
                CreateDirectories();
                ConvertExtensionsToLowerCase();
                ConvertBinToWav();
                
                RenameFiles();
                MoveDirectories();
                RemoveEmptyDirectories();
                MoveOriginalFiles();
                
            });
            Log("process complete");
            await CloseLogAsync();
        }



        private void ClearRenameOutput(object? sender, RoutedEventArgs e)
        {
            OutputBox!.Text = "";
            OutputBox.Text = "log cleared";
        }

        private void ExitApp(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private bool FindManifest()
        {
            var manifestPath = Path.Combine(imageDirectory!, "nimbie.txt");
            if (File.Exists(manifestPath))
            {
                manifestFile = manifestPath;
                return true;
            }
            else
            {
                Log($"{manifestFile} missing");
                return false;
            }
        }

        private bool ValidateManifest()
        {

            filenames = File.ReadLines(manifestFile!).ToList();
            if (filenames.Count == 0)
            {
                Log("Manifest file is empty.");
                return false;
            }
            foreach (var filename in filenames)
            {
                Log($"{filename}");
            }

            var imgCount = Directory.EnumerateFiles(imageDirectory!)
                .Count(file => file.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

            if (filenames.Count != imgCount)
            {
                Log($"Manifest file count ({filenames.Count}) does not match image file count ({imgCount}).");
                return false;
            }


            Log($"Manifest file count ({filenames.Count}) matches image file count ({imgCount}).");
            return true;
        }

        private void RemoveArtifacts()
        {
            var restrictedExtensions = new List<string>() { ".ccd", ".DVD", ".MDS" };
            var artifacts = Directory
                .GetFiles(imageDirectory!)
                .Where(f => restrictedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            foreach (var artifact in artifacts)
            {
                if (testMode)
                {
                    Log($"[TEST] removing {artifact}");
                }
                else
                {
                    Log($"removing {artifact}");
                    File.Delete(artifact);
                }
            }
        }

        private void ConvertExtensionsToLowerCase()
        {
            var files = Directory.GetFiles(imageDirectory!);
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (extension != null && extension.Any(char.IsUpper))
                {
                    var newFileName = Path.Combine(Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + extension.ToLower());
                    Log($"renaming {file} to {newFileName}");
                    File.Move(file, newFileName);
                }
            }
        }

        static bool DetermineIfBinIsAudio(string binPath)
        {
            var directory = Path.GetDirectoryName(binPath);
            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(binPath);
            var cuePath = Path.Combine(directory!, filenameWithoutExtension + ".cue");

            Console.WriteLine(Path.GetFileName(cuePath));

            var track = File.ReadLines(cuePath)
                .FirstOrDefault(l => l.TrimStart()
                    .StartsWith("TRACK 01 ", StringComparison.OrdinalIgnoreCase));

            if (track == null)
                return false;

            var parts = track.Split(
                (char[])null!,
                StringSplitOptions.RemoveEmptyEntries);

            return parts.Length >= 3 &&
                   parts[2].Equals("AUDIO", StringComparison.OrdinalIgnoreCase);
        }

        private void ConvertBinToWav()
        {
            var allowedExtensions = new List<string>() { ".img", ".bin" };
            var imgPaths = Directory
                .GetFiles(imageDirectory!)
                .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            foreach(string imgPath in imgPaths)
            {
                string wavPath = "";
                if(Path.GetExtension(imgPath) == ".img")
                {
                    wavPath = imgPath.Replace(".img", ".wav");
                } else if (Path.GetExtension(imgPath) == ".bin") {
                    wavPath = imgPath.Replace(".bin", ".wav");
                }

                if(DetermineIfBinIsAudio(imgPath)) 
                {
                    Log($"converting {imgPath} to {wavPath}");
                    byte[] audioData = File.ReadAllBytes(imgPath);
                    using (var fs = new FileStream(wavPath, FileMode.Create))
                    using (var bw = new BinaryWriter(fs))
                    {
                        int byteRate = sampleRate * channels * bitsPerSample / 8;
                        short blockAlign = (short)(channels * bitsPerSample / 8);

                        // WAV header
                        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                        bw.Write(36 + audioData.Length);
                        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

                        // fmt subchunk
                        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                        bw.Write(16); // PCM
                        bw.Write((short)1); // PCM format
                        bw.Write(channels);
                        bw.Write(sampleRate);
                        bw.Write(byteRate);
                        bw.Write(blockAlign);
                        bw.Write(bitsPerSample);

                        // data subchunk
                        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                        bw.Write(audioData.Length);
                        bw.Write(audioData);
                    }

                    File.SetCreationTimeUtc(wavPath, File.GetCreationTimeUtc(imgPath));
                    File.SetLastWriteTimeUtc(wavPath, File.GetLastWriteTimeUtc(imgPath));
                    File.SetLastAccessTimeUtc(wavPath, File.GetLastAccessTimeUtc(imgPath));

                    //move the bin file to the original directory
                    var targetFile = Path.Combine(OriginalDir!, Path.GetFileName(imgPath));
                    Console.WriteLine($"Moving {imgPath} to {targetFile}");
                    File.Move(imgPath, targetFile);
                }
            }
        }

        private void CreateDirectories()
        {
            OriginalDir = Path.Combine(imageDirectory!, "original");        
            Directory.CreateDirectory(OriginalDir);

            filenames = File.ReadLines(manifestFile!).ToList();

            HashSet<string> directories = new HashSet<String>();
            foreach (string filename in filenames)
            {
                directories.Add(Path.Combine(imageDirectory!, filename));
            }

            foreach (string directoryName in directories)
            {
                var targetDirectory = Path.Combine(imageDirectory!, directoryName);
                if (testMode) { Log($"[TEST] creating directory: {targetDirectory}"); }
                else
                {
                    Log($"creating directory: {targetDirectory}");
                    Directory.CreateDirectory(targetDirectory);
                }
            }

            foreach (string metaFormat in metaFormats) {
                if (testMode) { Log($"[TEST] creating directory: {metaFormat}"); }
                else
                {
                    Log($"creating directory: {metaFormat}");
                    Directory.CreateDirectory(Path.Combine(imageDirectory!, metaFormat));
                }
                
            }

        }

        private void RenameFiles()
        {
            var allowedExtensions = new[] { ".iso", ".wav", ".bin" };
            var imageFiles = Directory
                .GetFiles(imageDirectory!)
                .Where(f => allowedExtensions.Contains(
                    Path.GetExtension(f),
                    StringComparer.OrdinalIgnoreCase))
                .ToDictionary(f => f, f => File.GetLastWriteTime(f));

            
            
            var sortedFileDates = imageFiles.OrderBy(x => x.Value).ToList();
            for (int i = 0; i < sortedFileDates.Count; i++)
            {
                var newFilename = filenames![i];
                var fileDate = sortedFileDates[i];
                var originalPath = fileDate.Key;
                var originalTimeStamp = fileDate.Value;
                var originalFilename = Path.GetFileName(originalPath);
                var originalExtension = Path.GetExtension(originalPath).ToLower();
                var targetFilename = newFilename + originalExtension;
                var targetPath = Path.Combine(imageDirectory!, newFilename, targetFilename);
                CopyFile(originalPath, targetPath, originalTimeStamp);
                WriteMD5File(targetPath);


                if(originalExtension == ".wav")
                {
                    var originalCueFile = originalFilename.Replace(".wav", ".cue");
                    UpdateCueFile(originalCueFile, newFilename);
                    imageFormats.Add(newFilename, "audio");

                    //check for a cuefile
                    var originalCDTPath = Path.Join(originalPath, originalFilename.Replace(".wav", ".cdt"));
                    if(Path.Exists(originalCDTPath))
                    {
                        var targetCDTFilename = newFilename + ".cdt";
                        var targetCDTFilepath = Path.Combine(imageDirectory!, newFilename, targetCDTFilename);
                        CopyFile(originalCDTPath, targetCDTFilepath, originalTimeStamp);
                        
                    }

                    
                } else
                {
                    if (IsDVD(targetPath))
                    {
                        imageFormats.Add(newFilename, "video");
                        
                        generateCue(targetPath!);
                    } else
                    {
                        imageFormats.Add(newFilename, "data");
                        ///move the cue and md5 if they exist
                        string cuePath = Path.ChangeExtension(originalPath, ".CUE");
                        if(File.Exists(cuePath))
                        {
                            var targetCUEPath = Path.ChangeExtension(targetPath, ".cue");
                            CopyFile(cuePath, targetCUEPath, originalTimeStamp);
                            Log($"updating {targetCUEPath}");
                            var lines = File.ReadAllLines(targetCUEPath);
                            lines = Array.FindAll(lines, line => !line.TrimStart().StartsWith("CDTEXTFILE", StringComparison.OrdinalIgnoreCase));
                            lines = lines.Select(line =>
                            {
                                if (line.TrimStart().StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
                                    return $"FILE \"{newFilename}.iso\" BINARY";
                                return line;
                            }).ToArray();

                            File.WriteAllLines(targetCUEPath, lines);
                        }
                    }
                }
            }
        }

        private void MoveDirectories()
        {
            foreach (KeyValuePair<string, string> imgFormat in imageFormats)
            {
                var sourceDirectory = Path.Combine(imageDirectory!, imgFormat.Key);
                var targetDirectory = Path.Combine(imageDirectory!, imgFormat.Value, imgFormat.Key);
                Log($"Moving {sourceDirectory} to {targetDirectory}");
                Directory.Move(sourceDirectory, targetDirectory);
                
            }
        }

        private void RemoveEmptyDirectories()
        {
            var subDirectories = Directory.GetDirectories(imageDirectory!);

            foreach (var subDirectory in subDirectories)
            {
                if (!Directory.EnumerateFileSystemEntries(subDirectory).Any())
                {
                    if (testMode)
                    {
                        Log($"[TEST] removing empty directory: {subDirectory}");
                    }
                    else
                    {
                        Log($"removing empty directory: {subDirectory}");
                        Directory.Delete(subDirectory);
                    }
                }
            }
        }

        private void MoveOriginalFiles()
        {
            

            foreach (var file in Directory.GetFiles(imageDirectory!))
            {
                if (Path.GetFileName(file).Equals("nimbie-rename.log", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var newPath = Path.Combine(OriginalDir!, Path.GetFileName(file));

                Log($"moving {file} to {newPath}");

                File.Move(file, newPath);
            }
        }

        

        private string WriteMD5File(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            byte[] hash = MD5.HashData(stream);
            string md5 = Convert.ToHexString(hash).ToLowerInvariant();
            var md5Path = Path.ChangeExtension(filePath, ".md5");
            var md5Line = $"{md5}  {Path.GetFileName(filePath)}";
            File.WriteAllText(md5Path, md5Line);
            Log($"generated md5 `{md5}` for {Path.GetFileName(filePath)}");
            return md5;
        }

        private void CopyFile(string originalPath, string newPath, DateTime timestamp)
        {
            Log($"copying {originalPath} ({timestamp}) to {newPath}");
            try
            {
                File.Copy(originalPath, newPath);
            }
            catch (IOException ioEx)
            {
                Log($"IO error: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                Log($"Access denied: {uaEx.Message}");
            }
            catch (Exception ex)
            {
                Log($"Unexpected error: {ex.Message}");
            }
        }

        private void UpdateCueFile(string cueFile, string newFilename)
        {
            var originalPath = Path.Combine(imageDirectory!, cueFile);
            var cuePath = Path.Combine(imageDirectory!, newFilename, newFilename + ".cue");
            var wavFile = newFilename + ".wav";
            File.Copy(originalPath, cuePath);
            Log($"updating {cuePath}");
            var lines = File.ReadAllLines(cuePath);
            lines = Array.FindAll(lines, line => !line.TrimStart().StartsWith("CDTEXTFILE", StringComparison.OrdinalIgnoreCase));
            lines = lines.Select(line =>
            {
                if (line.TrimStart().StartsWith("FILE ", StringComparison.OrdinalIgnoreCase))
                    return $"FILE \"{wavFile}\" WAVE";
                return line;
            }).ToArray();

            File.WriteAllLines(cuePath, lines);
        }

        private bool IsDVD(string isoPath)
        {
            using var isoStream = File.OpenRead(isoPath);

            if (!CDReader.Detect(isoStream))
                return false;

            isoStream.Position = 0;

            using var cd = new CDReader(isoStream, true);

            return cd.GetDirectories(@"\").Any(dir =>
                string.Equals(Path.GetFileName(dir), "VIDEO_TS", StringComparison.OrdinalIgnoreCase));
        }

        private void generateCue(string isoPath)
        {
            Log(isoPath);

            if (!File.Exists(isoPath))
                throw new FileNotFoundException("ISO file not found.", isoPath);

            string isoFileName = Path.GetFileName(isoPath);
            string cuePath = Path.ChangeExtension(isoPath, ".cue");

            string cueContent =
$@"FILE ""{isoFileName}"" BINARY
  TRACK 01 MODE1/2048";

            File.WriteAllText(cuePath, cueContent);

            Log($"Created: {cuePath}");
        }

        // medialog update functions
        private async void PickUpdateFolder(object? sender, RoutedEventArgs e)
        {
            if (StorageProvider is null)
            {
                UpdateOutputBox.Text += "StorageProvider is not available.";
                return;
            }

            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false
            });

            if (folder != null && folder.Count > 0)
            {
                UpdatePathBox.Text = folder[0].Path.LocalPath;
                var logLocation = Path.Combine(UpdatePathBox.Text!, UpdateLogFileName);
                SetLog(sender, e, logLocation, UpdateOutputBox);
                logVersion(sender, e, logLocation, "update");
            }
        }
        private async void UpdateMedialog(object? sender, RoutedEventArgs e)
        {
            Log("Updating medialog");
            if (string.IsNullOrWhiteSpace(UpdatePathBox.Text))
            {
                return;
            }
            updateDirectory = UpdatePathBox.Text;

            if (string.IsNullOrWhiteSpace(updateDirectory))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(NimbieUserBox.Text))
            {
                Log("Username can not be blank");
                return;
            }

            if (string.IsNullOrWhiteSpace(NimbieBoxNum.Text))
            {
                Log("box number can not be blank");
                return;
            }

            Medialog medialog = new(Log);
            await medialog.UpdateMedialog(updateDirectory, NimbieUserBox.Text, NimbieBoxNum.Text!);
            await CloseLogAsync();

        }

    }


}

