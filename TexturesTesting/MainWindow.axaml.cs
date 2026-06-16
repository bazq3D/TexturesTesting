using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Salaros.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TexturesTesting;

public partial class MainWindow : Window
{
    private static string? _vPath;
    private GameFileCache _gameFileCache;
    private static readonly ExtractTask _globalExtractTask = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _selectedFileNames = new();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

    private string config = AppDomain.CurrentDomain.BaseDirectory + @"config.ini";

    public MainWindow()
    {
        InitializeComponent();
        ToggleControls(false);
        BtnLookEnts.IsEnabled = false;
        LbSelectedFiles.ItemsSource = _selectedFileNames;
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        await InitializeGtaPathOnStartup();
    }

    private async Task InitializeGtaPathOnStartup()
    {
        var cfg = new ConfigParser(config);
        string? configGtaPath = cfg.GetValue("CONFIG", "GTA5Path");
        string? targetPath = null;

        if (!string.IsNullOrEmpty(configGtaPath) && IsGtaPathValid(configGtaPath))
        {
            targetPath = configGtaPath.Trim(' ', '"', '\'', '\r', '\n');
        }
        else
        {
            var detectedPath = DetectGta5Path();
            if (!string.IsNullOrEmpty(detectedPath))
            {
                targetPath = detectedPath.Trim(' ', '"', '\'', '\r', '\n');
                cfg.SetValue("CONFIG", "GTA5Path", targetPath);
                cfg.Save();
            }
        }

        if (!string.IsNullOrEmpty(targetPath) && IsGtaPathValid(targetPath))
        {
            _vPath = targetPath.Trim(' ', '"', '\'', '\r', '\n');
            var loadMods = false;
            var questionBox = MessageBoxManager.GetMessageBoxStandard("Information", "Do you want to enable mods?",
                ButtonEnum.YesNo,
                MsBox.Avalonia.Enums.Icon.Info);
            var result = await questionBox.ShowAsync();
            GTA5Keys.LoadFromPath(_vPath);
            labelCache.Content = "Loading...";
            if (result == ButtonResult.Yes)
            {
                loadMods = true;
            }
            _gameFileCache = new GameFileCache(int.MaxValue, 10, _vPath, "mp2024_01_g9ec", loadMods, "Installers;_CommonRedist")
            {
                LoadAudio = false,
                LoadVehicles = false,
                LoadPeds = false
            };
            await Task.Run(() => _gameFileCache.Init(UpdateStatusCache, UpdateErrorLog));
            if (!_gameFileCache.IsInited) return;
            ToggleControls(true);
            labelCache.Content = "Game Cache Loaded";
            BtnGTAPath.IsEnabled = false;
        }
    }

    private void ToggleControls(bool state)
    {
        //Terrible coding right here, I'm sorry.
        cbExtractTextures.IsEnabled = state;
        cbExtractXml.IsEnabled = state;
        CBoxExtractType.IsEnabled = state;
        BtnLookfor.IsEnabled = state;
    }

    private async void BtnGTAPath_OnClick(object? sender, RoutedEventArgs e)
    {
        var cfg = new ConfigParser(config);

        // Check if config value exists
        string? configGtaPath = cfg.GetValue("CONFIG", "GTA5Path");

        if (!string.IsNullOrEmpty(configGtaPath) && IsGtaPathValid(configGtaPath))
        {
            _vPath = configGtaPath.Trim(' ', '"', '\'', '\r', '\n');
        }
        else
        {
            var detectedPath = DetectGta5Path();
            if (!string.IsNullOrEmpty(detectedPath))
            {
                _vPath = detectedPath.Trim(' ', '"', '\'', '\r', '\n');
                cfg.SetValue("CONFIG", "GTA5Path", _vPath);
                cfg.Save();
            }
            else
            {
                var selectGtaPath = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
                {
                    Title = "Select your GTA V Path",
                    AllowMultiple = false,
                });

                if (selectGtaPath.Count == 0) return;
                _vPath = selectGtaPath[0].Path.LocalPath.Trim(' ', '"', '\'', '\r', '\n');
                cfg.SetValue("CONFIG", "GTA5Path", _vPath);
                cfg.Save();
            }
        }

        if (IsGtaPathValid(_vPath))
        {
            var loadMods = false;
            var questionBox = MessageBoxManager.GetMessageBoxStandard("Information", "Do you want to enable mods?",
                ButtonEnum.YesNo,
                MsBox.Avalonia.Enums.Icon.Info);
            var result = await questionBox.ShowAsync();
            GTA5Keys.LoadFromPath(_vPath);
            labelCache.Content = "Loading...";
            if (result == ButtonResult.Yes)
            {
                loadMods = true;
            }
            _gameFileCache = new GameFileCache(int.MaxValue, 10, _vPath, "mp2024_01_g9ec", loadMods, "Installers;_CommonRedist")
            {
                LoadAudio = false,
                LoadVehicles = false,
                LoadPeds = false
            };
            await Task.Run(() => _gameFileCache.Init(UpdateStatusCache, UpdateErrorLog));
            if (!_gameFileCache.IsInited) return;
            ToggleControls(true);
            labelCache.Content = "Game Cache Loaded";
            BtnGTAPath.IsEnabled = false;
        }
        else
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Error", "Invalid GTA5 directory", ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error);
            SystemSoundPlayer.PlaySystemSound(SystemSoundType.Hand);
            await box.ShowAsync();
        }
    }

    private static bool IsGtaPathValid(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        path = path.Trim(' ', '"', '\'', '\r', '\n');
        return File.Exists(Path.Combine(path, "GTA5.exe"));
    }

    private static string? DetectGta5Path()
    {
#pragma warning disable CA1416
        try
        {
            // 1. Check Rockstar Games Registry
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Rockstar Games\Grand Theft Auto V"))
            {
                var path = key?.GetValue("InstallFolder") as string;
                if (!string.IsNullOrEmpty(path) && IsGtaPathValid(path)) return path.Trim(' ', '"', '\'', '\r', '\n');
            }

            // 2. Check Steam Registry for Steam install path, then check steamapps/common/Grand Theft Auto V and libraryfolders.vdf
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam"))
            {
                var steamPath = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    steamPath = steamPath.Trim(' ', '"', '\'', '\r', '\n');
                    var path = Path.Combine(steamPath, "steamapps", "common", "Grand Theft Auto V");
                    if (IsGtaPathValid(path)) return path;

                    var libraryFoldersVdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraryFoldersVdf))
                    {
                        try
                        {
                            var vdfContent = File.ReadAllText(libraryFoldersVdf);
                            var matches = System.Text.RegularExpressions.Regex.Matches(vdfContent, @"\""path\""\s*\""([^\""]+)\""");
                            foreach (System.Text.RegularExpressions.Match match in matches)
                            {
                                if (match.Success)
                                {
                                    var libPath = match.Groups[1].Value.Replace(@"\\", @"\").Trim(' ', '"', '\'', '\r', '\n');
                                    var customPath = Path.Combine(libPath, "steamapps", "common", "Grand Theft Auto V");
                                    if (IsGtaPathValid(customPath)) return customPath;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            // 3. Epic Games Store install detection
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var epicManifestsFolder = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(epicManifestsFolder))
            {
                foreach (var file in Directory.GetFiles(epicManifestsFolder, "*.item"))
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        if (content.Contains("\"MandatoryAppFolderName\": \"GTAV\"") || content.Contains("\"AppName\": \"GrandTheftAutoV\""))
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(content, @"\""InstallLocation\""\s*:\s*\""([^\""]+)\""");
                            if (match.Success)
                            {
                                var path = match.Groups[1].Value.Replace(@"\\", @"\").Trim(' ', '"', '\'', '\r', '\n');
                                if (IsGtaPathValid(path)) return path;
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return null;
#pragma warning restore CA1416
    }

    private void UpdateStatusCache(string text)
    {
        Dispatcher.UIThread.InvokeAsync(() => { labelCache.Content = text; });
    }

    private void UpdateOverallProgress(double value, double max, string statusText)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PbOverall.Maximum = max;
            PbOverall.Value = value;
            LblOverallStatus.Content = statusText;
        });
    }

    private void UpdateItemsProgress(double value, double max, string statusText)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PbItems.Maximum = max;
            PbItems.Value = value;
            LblItemsStatus.Content = statusText;
        });
    }
    
    private void UpdateErrorLog(string text)
    {
        Console.WriteLine(text);
    }

    private static async Task WriteTexturesAsync(IEnumerable<Texture> textures, string outFolder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outFolder);

        await Parallel.ForEachAsync(textures, ct, async (tex, token) =>
        {
            try
            {
                var fpath = Path.Combine(outFolder, $"{tex.Name}.dds");
                var dds = DDSIO.GetDDSFile(tex);
                await File.WriteAllBytesAsync(fpath, dds, token);
            }
            catch
            { }
        });
    }
    private async void BtnLookEnts_OnClick(object? sender, RoutedEventArgs e)
    {
        ToggleControls(false);
        BtnLookEnts.IsEnabled = false;

        try
        {
            if (_globalExtractTask.MapFiles.Count > 0)
            {
                var selectFolder = await GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions { Title = "Select the folder where you want to save the files", AllowMultiple = false });

                if (selectFolder is null || selectFolder.Count == 0)
                {
                    ToggleControls(true);
                    BtnLookEnts.IsEnabled = true;
                    return;
                }

                string? outputPath = selectFolder[0].Path.LocalPath;
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    ToggleControls(true);
                    BtnLookEnts.IsEnabled = true;
                    return;
                }

                bool extractXml = cbExtractXml.IsChecked ?? false;
                bool extractTextures = cbExtractTextures.IsChecked ?? false;

                // Reset progress bars
                UpdateOverallProgress(0, _globalExtractTask.MapFiles.Count, "Starting extraction...");
                UpdateItemsProgress(0, 100, "Waiting...");

                // Copy to process on background thread
                var mapFilesToProcess = new List<MapTask>(_globalExtractTask.MapFiles);

                await Task.Run(async () =>
                {
                    int totalMaps = mapFilesToProcess.Count;
                    int currentMapIdx = 0;

                    foreach (var mapFile in mapFilesToProcess)
                    {
                        currentMapIdx++;
                        UpdateOverallProgress(currentMapIdx - 1, totalMaps, $"Processing: {mapFile.FileName} ({currentMapIdx}/{totalMaps})");

                        var ymapFolderPath = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(mapFile.FileName) ?? "");
                        Directory.CreateDirectory(ymapFolderPath);

                        int totalEntities = mapFile.EntsHashes.Count;
                        int currentEntityIdx = 0;

                        if (totalEntities == 0)
                        {
                            UpdateItemsProgress(0, 100, "No entities in this file");
                        }

                        foreach (var entity in mapFile.EntsHashes)
                        {
                            currentEntityIdx++;

                            // Resolve entity name for the status label
                            string entityName = $"0x{entity:X8}";
                            var arch = _gameFileCache.GetArchetype(entity);
                            if (arch != null && arch.Name != null)
                            {
                                entityName = arch.Name;
                            }

                            UpdateItemsProgress(currentEntityIdx, totalEntities, $"Extracting: {entityName} ({currentEntityIdx}/{totalEntities})");
                            UpdateStatusCache($"Map {currentMapIdx}/{totalMaps} - {entityName}");

                            ModelType mt = new();
                            if (_gameFileCache.GetYdr(entity) != null)
                            {
                                var ydr = _gameFileCache.GetYdr(entity);
                                ydr.Load(ydr.RpfFileEntry.File.ExtractFile(ydr.RpfFileEntry), ydr.RpfFileEntry);
                                mt.YdrFiles.Add(ydr);
                            }

                            if (_gameFileCache.GetYdd(GetYddFromHash(entity)) != null)
                            {
                                var ydd = _gameFileCache.GetYdd(GetYddFromHash(entity));
                                ydd.Load(ydd.RpfFileEntry.File.ExtractFile(ydd.RpfFileEntry), ydd.RpfFileEntry);
                                mt.YddFiles.Add(ydd);
                            }

                            if (_gameFileCache.GetYft(entity) != null)
                            {
                                var yft = _gameFileCache.GetYft(entity);
                                yft.Load(yft.RpfFileEntry.File.ExtractFile(yft.RpfFileEntry), yft.RpfFileEntry);
                                mt.YftFiles.Add(yft);
                            }

                            if (mt.YdrFiles.Count > 0)
                            {
                                foreach (var mYdr in mt.YdrFiles)
                                {
                                    if (!extractXml)
                                    {
                                        await File.WriteAllBytesAsync(Path.Combine(ymapFolderPath, mYdr.Name), mYdr.Save());
                                    }
                                    else
                                    {
                                        var ydrXml = MetaXml.GetXml(mYdr, filename: out var ydrName,
                                            Path.Combine(ymapFolderPath, mYdr.Name.Split(".")[0]));
                                        await File.WriteAllTextAsync(Path.Combine(ymapFolderPath, $"{mYdr.Name}.xml"), ydrXml);
                                    }

                                    if (!extractTextures) continue;
                                    var textures = new HashSet<Texture>();
                                    var textureMissing = new HashSet<string>();
                                    var extract = Directory.CreateDirectory(Path.Combine(ymapFolderPath, "alltextures"));
                                    if (mYdr.Drawable != null)
                                    {
                                        CollectTextures(mYdr.Drawable, textures, textureMissing);
                                    }

                                    await WriteTexturesAsync(textures, extract.FullName);
                                }
                            }

                            if (mt.YddFiles.Count > 0)
                            {
                                foreach (var mYdd in mt.YddFiles)
                                {
                                    if (!extractXml)
                                    {
                                        await File.WriteAllBytesAsync(Path.Combine(ymapFolderPath, mYdd.Name), mYdd.Save());
                                    }
                                    else
                                    {
                                        var yddXml = MetaXml.GetXml(mYdd, filename: out var yddName,
                                            Path.Combine(ymapFolderPath, mYdd.Name.Split(".")[0]));
                                        await File.WriteAllTextAsync(Path.Combine(ymapFolderPath, $"{mYdd.Name}.xml"), yddXml);
                                    }

                                    if (!extractTextures) continue;
                                    var textures = new HashSet<Texture>();
                                    var textureMissing = new HashSet<string>();
                                    var extract = Directory.CreateDirectory(Path.Combine(ymapFolderPath, "alltextures"));
                                    if (mYdd.DrawableDict != null)
                                    {
                                        foreach (var dd in mYdd.Drawables)
                                        {
                                            CollectTextures(dd, textures, textureMissing);
                                        }
                                    }

                                    await WriteTexturesAsync(textures, extract.FullName);
                                }
                            }

                            if (mt.YftFiles.Count > 0)
                            {
                                foreach (var mYft in mt.YftFiles)
                                {
                                    if (!extractXml)
                                    {
                                        await File.WriteAllBytesAsync(Path.Combine(ymapFolderPath, mYft.Name), mYft.Save());
                                    }
                                    else
                                    {
                                        var yftXml = MetaXml.GetXml(mYft, filename: out var yftName,
                                            Path.Combine(ymapFolderPath, mYft.Name.Split(".")[0]));
                                        await File.WriteAllTextAsync(Path.Combine(ymapFolderPath, $"{mYft.Name}.xml"), yftXml);
                                    }

                                    if (!extractTextures) continue;
                                    var textures = new HashSet<Texture>();
                                    var textureMissing = new HashSet<string>();
                                    var extract = Directory.CreateDirectory(Path.Combine(ymapFolderPath, "alltextures"));
                                    CollectTextures(mYft.Fragment.Drawable, textures, textureMissing);

                                    await WriteTexturesAsync(textures, extract.FullName);
                                }
                            }
                        }

                        // Set current map progress to 100% after finished
                        UpdateItemsProgress(totalEntities, totalEntities, $"Finished: {mapFile.FileName}");
                    }

                    UpdateOverallProgress(totalMaps, totalMaps, "All files processed successfully.");
                });

                // Clear the map files queue
                _selectedFileNames.Clear();
                _globalExtractTask.MapFiles.Clear();

                // Play system sound and flash window to notify completion
                SystemSoundPlayer.PlaySystemSound(SystemSoundType.Question);
                var hwnd = this.TryGetPlatformHandle()?.Handle;
                if (hwnd.HasValue)
                {
                    FlashWindow(hwnd.Value, true);
                }

                var msBoxExtract = MessageBoxManager.GetMessageBoxStandard($"Information", $"Extraction Completed",
                    ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                await msBoxExtract.ShowAsync();
            }
            else
            {
                var noEntsMsg = MessageBoxManager.GetMessageBoxStandard($"Information", $"No Entities Detected",
                    ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                await noEntsMsg.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            var errMsg = MessageBoxManager.GetMessageBoxStandard($"Error", $"Extraction failed: {ex.Message}",
                ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error, WindowStartupLocation.CenterScreen);
            await errMsg.ShowAsync();
        }
        finally
        {
            ToggleControls(true);
            BtnLookEnts.IsEnabled = false;
            UpdateStatusCache("Ready");
        }
    }

    private void MiExit_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static uint ToUInt(MetaHash h) => unchecked((uint)h);
    private void CollectTextures(DrawableBase d, ISet<Texture> textureSet, ISet<string> textureMissing)
    {
        var sg = d?.ShaderGroup;
        if (sg == null) return;

        var dictTextures = sg.TextureDictionary?.Textures?.data_items;
        if (dictTextures != null)
        {
            foreach (var tex in dictTextures)
            {
                if (tex != null) textureSet.Add(tex);
            }
        }

        var shaders = sg.Shaders?.data_items;
        if (shaders == null) return;

        uint archhash = 0u;
        switch (d)
        {
            case Drawable dwbl:
                {
                    var name = dwbl.Name ?? string.Empty;
                    int dot = name.IndexOf('.');
                    string raw = dot >= 0 ? name.Substring(0, dot) : name;
                    string lowered = raw.ToLowerInvariant();
                    archhash = JenkHash.GenHash(lowered);
                    break;
                }
            case FragDrawable fdbl:
                {
                    var yft = fdbl.Owner as YftFile;
                    MetaHash fraghash = yft?.RpfFileEntry?.ShortNameHash ?? 0;
                    archhash = fraghash;
                    break;
                }
        }

        Archetype arch = _gameFileCache.GetArchetype(archhash);
        if (arch == null) return;

        uint txdHash = arch.TextureDict != null ? ToUInt(arch.TextureDict.Hash) : archhash;

        var foundCache = new Dictionary<ulong, Texture>(64);
        var parentCache = new Dictionary<uint, uint>(8);

        uint GetParentTxd(uint h)
        {
            if (h == 0) return 0;
            if (parentCache.TryGetValue(h, out var p)) return p;
            p = _gameFileCache.TryGetParentYtdHash(h);
            parentCache[h] = p;
            return p;
        }

        Texture TryResolve(uint texHash, uint startTxd)
        {
            if (texHash == 0) return null;

            ulong makeKey(uint th, uint txd) => ((ulong)txd << 32) | th;

            for (uint cur = startTxd; cur != 0; cur = GetParentTxd(cur))
            {
                var key = makeKey(texHash, cur);
                if (foundCache.TryGetValue(key, out var cached)) return cached;

                var tex = TryGetTexture(texHash, cur);
                if (tex != null)
                {
                    foundCache[key] = tex;
                    return tex;
                }
                foundCache[key] = null;
            }

            {
                var key = makeKey(texHash, 0);
                if (foundCache.TryGetValue(key, out var cached)) return cached;

                var ytd = _gameFileCache.TryGetTextureDictForTexture(texHash);
                var tex = TryGetTextureFromYtd(texHash, ytd);
                foundCache[key] = tex; // may be null
                return tex;
            }
        }

        foreach (var s in shaders)
        {
            var plist = s?.ParametersList?.Parameters;
            if (plist == null) continue;

            foreach (var p in plist)
            {
                if (p?.Data is null) continue;

                if (p.Data is Texture concrete)
                {
                    textureSet.Add(concrete);
                    continue;
                }

                if (p.Data is TextureBase tb)
                {
                    var resolved = TryResolve(tb.NameHash, txdHash);
                    if (resolved != null)
                    {
                        textureSet.Add(resolved);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(tb.Name))
                            textureMissing.Add(tb.Name);
                    }
                }
            }
        }
    }

    private Texture? TryGetTexture(uint texHash, uint txdHash)
    {
        if (txdHash == 0 || texHash == 0) return null;
        var ytd = _gameFileCache.GetYtd(txdHash);
        return TryGetTextureFromYtd(texHash, ytd);
    }

    private static Texture? TryGetTextureFromYtd(uint texHash, YtdFile? ytd)
    {
        if (ytd == null || texHash == 0) return null;
        if (ytd.TextureDict == null)
        {
            var entry = ytd.RpfFileEntry;
            if (entry?.File != null)
            {
                var data = entry.File.ExtractFile(entry);
                ytd.Load(data, entry);
            }
            else
            {
                ytd.Load(null, entry);
            }
        }

        return ytd.TextureDict?.Lookup(texHash);
    }

    private async void BtnLookfor_OnClick(object? sender, RoutedEventArgs e)
    {
        switch (CBoxExtractType.SelectedIndex)
        {
            case 0: // YMAPs

                var ymapResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = "Select YMAP(s) folder",
                        AllowMultiple = true,
                        FileTypeFilter = new[] { new FilePickerFileType("YMAP(s)") { Patterns = new[] { "*.ymap" }}}
                    });
                if (ymapResult.Count <= 0) return;
                var ymapMsgInfo = MessageBoxManager.GetMessageBoxStandard($"Information",
                    $"Detected {ymapResult.ToList().Count} YMAP(s)", ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                if (ymapResult.Any(x => x.Name.EndsWith(".ymap", StringComparison.OrdinalIgnoreCase)))
                {
                    await ymapMsgInfo.ShowAsync();
                    foreach (var ymap in ymapResult)
                    {
                        var path = ymap.Path.LocalPath;
                        var fileName = Path.GetFileName(path);
                        if (!_selectedFileNames.Contains(fileName))
                        {
                            _selectedFileNames.Add(fileName);
                            _globalExtractTask.MapFiles.Add(new MapTask(path, GetEntityHashesFromFile(path, 0)));
                        }
                    }
                    if (_globalExtractTask.MapFiles.Count > 0)
                    {
                        BtnLookEnts.IsEnabled = true;
                    }
                }

                break;
            case 1:
            case 3:
                var ytypResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = "Select YTYP(s) folder",
                        AllowMultiple = true,
                        FileTypeFilter = new[] { new FilePickerFileType("YTYP(s)") { Patterns = new[] { "*.ytyp" }}}
                    });

                var ytypMsgInfo = MessageBoxManager.GetMessageBoxStandard($"Information",
                    $"Detected {ytypResult.ToList().Count} YTYP(s)", ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                if (ytypResult.Count <= 0) return;
                if (ytypResult.Any(x => x.Name.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase)))
                {
                    await ytypMsgInfo.ShowAsync();
                    foreach (var ytyp in ytypResult)
                    {
                        try
                        {
                            var path = ytyp.Path.LocalPath;
                            var fileName = Path.GetFileName(path);
                            if (!_selectedFileNames.Contains(fileName))
                            {
                                var hashes = GetEntityHashesFromFile(path, 1, CBoxExtractType.SelectedIndex == 3);
                                _selectedFileNames.Add(fileName);
                                _globalExtractTask.MapFiles.Add(new MapTask(path, hashes));
                            }
                        }
                        catch (InvalidDataException ex)
                        {
                            Debug.WriteLine($"InvalidDataException for file {ytyp.Path.LocalPath}: {ex.Message}");
                        }
                    }
                    if (_globalExtractTask.MapFiles.Count > 0)
                    {
                        BtnLookEnts.IsEnabled = true;
                    }
                }

                break;
            case 2:
                var textFileResult = await GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(
                    new FilePickerOpenOptions()
                    {
                        Title = "Select Text File folder",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("Text File") { Patterns = new[] { "*.txt" }}}
                    });

                var textFileMsgInfo = MessageBoxManager.GetMessageBoxStandard($"Information", $"Valid Text File",
                    ButtonEnum.Ok,
                    MsBox.Avalonia.Enums.Icon.Info, WindowStartupLocation.CenterScreen);
                if (textFileResult.Count <= 0) return;
                if (textFileResult.Any(x => x.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    await textFileMsgInfo.ShowAsync();
                    var path = textFileResult[0].Path.LocalPath;
                    var fileName = Path.GetFileName(path);
                    if (!_selectedFileNames.Contains(fileName))
                    {
                        _selectedFileNames.Add(fileName);
                        _globalExtractTask.MapFiles.Add(new MapTask(path, GetEntityHashesFromFile(File.ReadAllLines(path))));
                    }
                    if (_globalExtractTask.MapFiles.Count > 0)
                    {
                        BtnLookEnts.IsEnabled = true;
                    }
                }

                break;
        }
    }

    private void BtnRemoveFile_OnClick(object? sender, RoutedEventArgs e)
    {
        var selectedIdx = LbSelectedFiles.SelectedIndex;
        if (selectedIdx >= 0 && selectedIdx < _selectedFileNames.Count)
        {
            _selectedFileNames.RemoveAt(selectedIdx);
            _globalExtractTask.MapFiles.RemoveAt(selectedIdx);
        }

        if (_globalExtractTask.MapFiles.Count == 0)
        {
            BtnLookEnts.IsEnabled = false;
        }
    }

    private void BtnClearFiles_OnClick(object? sender, RoutedEventArgs e)
    {
        _selectedFileNames.Clear();
        _globalExtractTask.MapFiles.Clear();
        BtnLookEnts.IsEnabled = false;
    }

    private static List<uint> GetEntityHashesFromFile(string file, int type, bool includeMloEntities = false)
    {
        List<uint> hashes = [];
        switch (type)
        {
            case 0:
                var ymapFile = new YmapFile();
                ymapFile.Load(File.ReadAllBytes(file));
                hashes.AddRange(ymapFile.AllEntities.Select(entity => entity._CEntityDef.archetypeName.Hash));
                return hashes.Distinct().ToList();
            case 1:
                var ytypFile = new YtypFile();
                ytypFile.Load(File.ReadAllBytes(file));
                hashes.AddRange(ytypFile.AllArchetypes.Select(archetype => archetype._BaseArchetypeDef.assetName.Hash));
                if (includeMloEntities)
                {
                    foreach (var archetype in ytypFile.AllArchetypes.Where(x => x.Type == MetaName.CMloArchetypeDef))
                    {
                        var mlo = (MloArchetype)archetype;
                        if (mlo?.entitySets != null && mlo.entitySets.Length != 0)
                        {
                            hashes.AddRange(
                                mlo.entitySets
                                    .SelectMany(entitySet => entitySet.Entities.Select(x => x.Data.archetypeName.Hash))
                            );
                        }
                        hashes.AddRange(mlo.entities.Select(x => x.Data.archetypeName.Hash));
                    }
                }

                return hashes.Distinct().ToList();
        }

        return hashes;
    }

    private static List<uint> GetEntityHashesFromFile(IEnumerable<string> textLines)
    {
        List<uint> hashes = textLines.Select(line => JenkHash.GenHash(line.ToLowerInvariant().Trim())).ToList();
        return hashes.Distinct().ToList();
    }

    private uint GetYddFromHash(uint hash)
    {
        var arch = _gameFileCache.GetArchetype(hash);
        return arch != null ? arch._BaseArchetypeDef.drawableDictionary.Hash : (uint)0;
    }
}