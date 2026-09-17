using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DiscUtils.Iso9660;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Nimbie_Rename_UI
{
    public partial class MainWindow : Window
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 2;
        bool testMode;
        string? imageDirectory;
        string? manifestFile;
        List<string>? filenames;
        List<string> metaFormats = new List<string>() {"audio", "video", "data" };
        Dictionary<string, string> imageFormats = new Dictionary<string, string>() { };

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void PickImageFolder(object? sender, RoutedEventArgs e)
        {
            if (StorageProvider is null)
            {
                Log("StorageProvider is not available.");
                return;
            }

            var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false
            });

            if (folder != null && folder.Count > 0)
                ImagePathBox.Text = folder[0].Path.LocalPath;
        }

        public async void ShowAbout(object? sender, RoutedEventArgs e)
        {
            Log("About");
        }

        private async void PickManifestFile(object? sender, RoutedEventArgs e)
        {
            if (StorageProvider is null)
            {
                Log("StorageProvider is not available.");
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = null // You can add filters if needed
            });
        }

        internal void Log(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                OutputBox.Text += $"{DateTime.Now:HH:mm:ss}  {message}\n";
            });
        }

        private void ClearLog(object? sender, RoutedEventArgs e)
        {
            OutputBox.Text = "";
        }

        private void SaveLog(object? sender, RoutedEventArgs e)
        {
            var imagePath = ImagePathBox.Text;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                Log("image path is empty. cannot save log.");
                return;
            }

            // Ensure the directory exists before creating the file
            if (!Directory.Exists(imagePath))
            {
                Log("image path does not exist. cannot save log.");
                return;
            }

            var logFilePath = Path.Combine(imagePath, "nimbie-rename.log");
            Log($"writing log file {logFilePath}");
            using var outputFile = File.Create(logFilePath);
            using var writer = new StreamWriter(outputFile);
            writer.Write(OutputBox.Text);

            Log($"log saved to {logFilePath}");
        }

        private void ExitApp(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }


        private async void RunProcess(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ImagePathBox.Text))
            {
                return;
            }
            imageDirectory = ImagePathBox.Text;

            if (string.IsNullOrWhiteSpace(imageDirectory))
            {
                return;
            }


            testMode = false;
            

            await Task.Run(() =>
            {
                if (testMode)
                {
                    Log("running in test mode");
                }

                if (!Directory.Exists(imageDirectory))
                {
                    Log("Invalid paths.");
                    return;
                }

                FindManifest();
                RemoveArtifacts();
                ConvertImgToWav();
                CreateDirectories();
                RenameFiles();
                MoveDirectories();
                RemoveEmptyDirectories();
                MoveOriginalFiles();
                UpdateMedialog();
                
            });
            SaveLog(sender, e);
            Log("done.");
        }

        private async void UpdateMedialog(object? sender, RoutedEventArgs e)
        {
            Log("Updating medialog");
            if (string.IsNullOrWhiteSpace(ImagePathBox.Text))
            {
                return;
            }
            imageDirectory = ImagePathBox.Text;

            if (string.IsNullOrWhiteSpace(imageDirectory))
            {
                return;
            }

            var nimbieUser = NimbieUserBox.Text ?? throw new Exception("Nimbe User Cannot Be Null");

            Medialog medialog = new (Log);
            await medialog.UpdateMedialog(imageDirectory, nimbieUser);
            
        }   

        private void FindManifest()
        {
            var manifestPath = Path.Combine(imageDirectory!, "nimbie.txt");
            if (File.Exists(manifestPath))
            {
                manifestFile = manifestPath;
            } else
            {
                Log($"Found {manifestFile}");
            }
        }

        private void RemoveArtifacts()
        {
            var restrictedExtensions = new List<string>() { ".cdt", ".ccd", ".DVD", ".MDS" };
            var artifacts = Directory
                .GetFiles(imageDirectory!)
                .Where(f => restrictedExtensions.Contains(Path.GetExtension(f)))
                .ToList();

            foreach (var artifact in artifacts)
            {
                if (testMode){
                    Log($"[TEST] removing {artifact}");
                } else
                {
                    Log($"removing {artifact}");
                    File.Delete(artifact);
                }     
            }
        }

        private void ConvertImgToWav()
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

                if (testMode)
                {
                    Log($"[TEST] converting {imgPath} to {wavPath}");
                }
                else
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
                }

            }
        }

        private void CreateDirectories()
        {

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
            var allowedExtensions = new[] { ".iso", ".wav" };
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

        private string WriteMD5File(string filePath)
        {
            Log($"DEBUG: {filePath}");
            byte[] hash = MD5.HashData(File.ReadAllBytes(filePath));
            string md5 = Convert.ToHexString(hash).ToLowerInvariant();
            Log($"DEBUG: {md5}");
            var md5Path = Path.ChangeExtension(filePath, ".md5");
            var md5Line = $"{md5}  {Path.GetFileName(filePath)}";
            Log($"DEBUG {md5Line}");
            File.WriteAllText(md5Path, md5Line);
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

        private void MoveDirectories()
        {
            foreach (KeyValuePair<string, string> imgFormat in imageFormats) {
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

        private void UpdateMedialog()
        {

        }

        private void MoveOriginalFiles()
        {
            var originalDir = Path.Combine(imageDirectory!, "original");
            Directory.CreateDirectory(originalDir);

            foreach (var file in Directory.GetFiles(imageDirectory!))
            {
                var newPath = Path.Combine(originalDir, Path.GetFileName(file));

                Log($"moving {file} to {newPath}");

                File.Move(file, newPath);
            }
        }

    }
}