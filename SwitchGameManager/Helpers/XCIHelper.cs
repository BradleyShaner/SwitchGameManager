﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using static hacbuild.XCIManager;

namespace SwitchGameManager.Helpers
{
    public static class XciHelper
    {
        private static BackgroundWorker backgroundWorkerFullLoad;
        private static BackgroundWorker backgroundWorkerSingleLoad;
        private static object cacheListLock = new object();
        private static hacbuild.XCI hac = new hacbuild.XCI();
        private static object pcListLock = new object();
        private static object refreshListLock = new object();
        private static object sdListLock = new object();
        private static List<XciItem> xciCache;
        private static List<XciItem> xciOnPc = new List<XciItem>();
        private static List<XciItem> xciOnSd = new List<XciItem>();
        private static List<XciItem> xciToRefresh = new List<XciItem>();
        public static formMain formMain;
        public static bool isGameLoadingComplete = false;
        public enum XciLocation
        {
            PC,
            SD
        }

        private static bool IsFileTransferInProgress()
        {
            if (FileHelper.isTransferInProgress)
            {
                formMain.UpdateToolStripLabel("A file transfer is in progress. Please try again after it has finished.");
                return true;
            }

            return false;
        }

        private static void LoadXcis()
        {
            bool updatedXciCache = false;
            
            Log($"LoadXcis");
            formMain.UpdateToolStripLabel("Loading games..");
            formMain.olvList.EmptyListMsg = "Loading games..";

            Application.DoEvents();

            lock (pcListLock)
                xciOnPc = GetPcXcis();
            lock (sdListLock)
                xciOnSd = GetSdXcis();

            int progressCount = xciOnPc.Count + xciOnSd.Count;
            int progress = 0;

            formMain.SetupProgressBar(0, progressCount, progress);

            lock (pcListLock)
            {
                for (int i = 0; i < xciOnPc.Count; i++)
                {
                    formMain.UpdateToolStripLabel($"Processing [{progress}/{progressCount}] {Path.GetFileName(xciOnPc[i].xciFilePath)} ");

                    xciOnPc[i] = RefreshGame(xciOnPc[i]);

                    if (Settings.config.defaultView == XciLocation.PC)
                        formMain.olvList.AddObject(xciOnPc[i]);

                    if (UpdateXciCache(xciOnPc[i]))
                        updatedXciCache = true;

                    progress++;
                    formMain.UpdateProgressBar(progress);
                }
            }

            lock (sdListLock)
            {
                for (int i = 0; i < xciOnSd.Count; i++)
                {
                    progress++;
                    formMain.UpdateProgressBar(progress);

                    formMain.UpdateToolStripLabel($"Processing [{progress}/{progressCount}] {Path.GetFileName(xciOnSd[i].xciFilePath)}");

                    xciOnSd[i] = RefreshGame(xciOnSd[i]);

                    xciOnSd[i].xciLocation = XciLocation.SD;

                    if (Settings.config.defaultView == XciLocation.SD)
                        formMain.olvList.AddObject(xciOnSd[i]);

                    if (UpdateXciCache(xciOnSd[i]))
                        updatedXciCache = true;
                }
            }

            if (updatedXciCache)
                SaveXciCache();

            formMain.HideProgressElements();
            formMain.UpdateToolStripLabel();

            isGameLoadingComplete = true;
        }

        internal static void RebuildCache()
        {
            if (IsFileTransferInProgress())
                return;

            lock (cacheListLock)
                xciCache = new List<XciItem>();

            File.Delete(Settings.cacheFileName);
            XciHelper.LoadXcisInBackground();
        }

        public static T Clone<T>(T source)
        {
            try
            {
                var serialized = JsonConvert.SerializeObject(source);
                return JsonConvert.DeserializeObject<T>(serialized);
            }
            catch { return default(T); }
        }

        public static List<string> FindAllFiles(string startDir, string filter, bool recurse = true)
        {
            List<string> files = new List<string>();

            if (recurse)
            {
                if (!Directory.Exists(startDir))
                    return files;

                foreach (var folder in Directory.GetDirectories(startDir))
                {
                    files.AddRange(FindAllFiles(folder, filter, recurse));
                }
            }

            files.AddRange(Directory.GetFiles(startDir, filter).ToList());

            return files;
        }

        public static XciItem FindXciByIdentifer(string uniqueId, List<XciItem> xciCache = null)
        {
            if (xciCache == null)
            {
                if (XciHelper.xciCache == null)
                    XciHelper.xciCache = LoadXciCache();

                xciCache = XciHelper.xciCache;
            }

            XciItem xci;
            try
            {
                xci = xciCache.First(item => item.uniqueId == uniqueId);
            }
            catch (Exception ex)
            {
                return null;
            }
            return xci;
        }

        /*
        public static XciItem FindXciByIdentifer(ulong packageId, List<XciItem> xciCache = null)
        {
            if (xciCache == null)
            {
                if (XciHelper.xciCache == null)
                    XciHelper.xciCache = LoadXciCache();

                xciCache = XciHelper.xciCache;
            }

            XciItem xci;
            try
            {
                xci = xciCache.First(item => item.packageId == packageId);
            }
            catch (Exception ex)
            {
                return null;
            }
            return xci;
        }
        
        
        public static List<XciItem> GetAllItemsByIdentifer(ulong packageId)
        {
            List<XciItem> xciList = new List<XciItem>();

            try
            {
                xciList.AddRange(xciOnPc.FindAll(item => item.packageId == packageId));
            }
            catch { }

            try
            {
                xciList.AddRange(xciOnSd.FindAll(item => item.packageId == packageId));
            }
            catch { }

            return xciList;
        }
        */

        public static List<XciItem> GetAllItemsByIdentifer(string uniqueId)
        {
            List<XciItem> xciList = new List<XciItem>();

            try
            {
                xciList.AddRange(xciOnPc.FindAll(item => item.uniqueId == uniqueId));
            }
            catch { }

            try
            {
                xciList.AddRange(xciOnSd.FindAll(item => item.uniqueId == uniqueId));
            }
            catch { }

            return xciList;
        }

        public static List<XciItem> GetPcXcis()
        {
            List<XciItem> xciList = new List<XciItem>();

            foreach (string path in Settings.config.localXciFolders)
            {
                xciList.AddRange(XciHelper.LoadGamesFromPath(path, recurse: true, isSdCard: false));
            }

            //xciList = XciHelper.CreateMasterXciList(xciList, xciOnSd);

            return xciList;
        }

        public static List<XciItem> GetSdXcis()
        {
            List<XciItem> xciList = new List<XciItem>();

            if (Directory.Exists(Settings.config.sdDriveLetter))
            {
                // SD card games are currently only in the root directory (for SX OS)
                xciList = XciHelper.LoadGamesFromPath(Settings.config.sdDriveLetter, recurse: false, isSdCard: true);
            }

            return xciList;
        }

        public static string GetXciIdentifier(string fileName)
        {
            if (!File.Exists(fileName))
                return "UNKNOWN";

            //xci_header header = hac.GetXCIHeader(fileName);
            
            return sha256(Path.GetFileNameWithoutExtension(fileName));

            //return header.PackageID;
        }

        /*
        public static XciItem GetXciInfoNew(string filePath, XciLocation location)
        {
            XciItem xci = new XciItem(filePath);

            if (!File.Exists(filePath))
                return null;

            return xci;

        }
        */

        public static XciItem GetXciInfo(string filePath, XciLocation location)
        {
            XciItem xci = new XciItem(filePath);

            Log($"GetXciInfo {filePath}");

            if (!File.Exists(filePath))
                return null;

            XCI_Explorer.MainForm mainForm = new XCI_Explorer.MainForm(false);

            //xci_header header = hac.GetXCIHeader(xci.xciFilePath);

            //xci.packageId = header.PackageID;

            xci.uniqueId = GetXciIdentifier(filePath);

            mainForm.SGM_ProcessFile(filePath);
            
            xci.gameName = mainForm.TB_Name.Text.Trim().TrimEnd('\0');
            xci.gameDeveloper = mainForm.TB_Dev.Text.Trim().TrimEnd('\0');
            xci.gameCardCapacity = mainForm.TB_Capacity.Text.Trim().TrimEnd('\0');
            xci.gameIcon = (Bitmap)mainForm.PB_GameIcon.BackgroundImage;
            xci.gameRevision = mainForm.TB_GameRev.Text.Trim().TrimEnd('\0');
            xci.masterKeyRevision = mainForm.TB_MKeyRev.Text.Trim().TrimEnd('\0');
            xci.sdkVersion = mainForm.TB_SDKVer.Text.Trim().TrimEnd('\0');
            xci.titleId = mainForm.TB_TID.Text.Trim().TrimEnd('\0');
            if (xci.titleId.Length != 16) xci.titleId = 0 + xci.titleId;
            xci.gameSize = mainForm.ExactSize;
            xci.gameUsedSize = mainForm.UsedSize;
            xci.productCode = mainForm.TB_ProdCode.Text.Trim().TrimEnd('\0');
            xci.gameCert = ReadXciCert(xci.xciFilePath);
            xci.xciFileSize = new System.IO.FileInfo(xci.xciFilePath).Length;
            

            if (location == XciLocation.PC)
            {
                xci.xciLocation = XciLocation.PC;
                xci.isGameOnPc = true;
            }
            else
            {
                xci.xciLocation = XciLocation.SD;
                xci.isGameOnSd = true;
            }

            // compare the expected size with the actual size
            xci.isXciTrimmed = (xci.gameSize == xci.gameUsedSize);

            // compare the first byte of the cert to the rest of the cert if they're all the same,
            // it's not unique. ex 255 for all
            xci.isUniqueCert = !xci.gameCert.All(s => s.Equals(xci.gameCert[0]));

            mainForm.Close();
            mainForm = null;

            return xci;
        }

        private static void Log(string text)
        {
            /*
            using (var tw = new StreamWriter("log.txt", true))
            {
                tw.WriteLine(DateTime.Now + " " + text);
            }
            */
        }

        public static bool IsXciInfoValid(XciItem xci)
        {
            if (xci == null)
                return false;

            if (string.IsNullOrWhiteSpace(xci.gameName))
                return false;

            if (string.IsNullOrWhiteSpace(xci.titleId))
                return false;

            return true;
        }

        public static List<XciItem> LoadGamesFromPath(string dirPath, bool recurse = true, bool isSdCard = false)
        {
            List<XciItem> pathXciList = new List<XciItem>();
            //ulong packageId;
            string uniqueId;
            XciItem xciTemp;

            Log($"LoadGamesFromPath: {dirPath}");
            List<string> xciFileList = FindAllFiles(dirPath, "*.xci", recurse);

            foreach (var item in xciFileList)
            {
                uniqueId = XciHelper.GetXciIdentifier(item);
                //Log($"LoadGamesFromPath: {uniqueId} - {item}");

                // Check if this game is in the Cache and Clone the cache XciItem to decouple the objects
                lock (cacheListLock)
                    xciTemp = Clone(XciHelper.FindXciByIdentifer(uniqueId));

                if (xciTemp == null)
                    xciTemp = new XciItem();

                xciTemp.xciFilePath = "";

                xciTemp.isGameOnSd = isSdCard;
                xciTemp.isGameOnPc = !isSdCard;

                if (isSdCard)
                    xciTemp.xciLocation = XciLocation.SD;
                else
                    xciTemp.xciLocation = XciLocation.PC;

                xciTemp.xciFilePath = item;

                pathXciList.Add(xciTemp);
            }

            return pathXciList;
        }

        public static List<XciItem> LoadXciCache(string fileName = "")
        {
            List<XciItem> cache = new List<XciItem>();

            Log($"LoadXciCache: {fileName}");
            lock (cacheListLock)
            {
                if (String.IsNullOrWhiteSpace(fileName))
                    fileName = Settings.cacheFileName;

                if (!File.Exists(fileName))
                    return cache;

                cache = JsonConvert.DeserializeObject<IEnumerable<XciItem>>(File.ReadAllText(fileName)).ToList<XciItem>();
            }

            return cache;
        }

        public static void LoadXcisInBackground()
        {
            IsFileTransferInProgress();
            Log($"LoadXcisInBackground: {IsFileTransferInProgress()}");

            if (backgroundWorkerFullLoad == null)
            {
                backgroundWorkerFullLoad = new BackgroundWorker();
                backgroundWorkerFullLoad.RunWorkerCompleted += delegate { formMain.locationToolStripComboBox.Enabled = isGameLoadingComplete; formMain.olvList.EmptyListMsg = "No Games Found!"; };
                backgroundWorkerFullLoad.DoWork += delegate { LoadXcis(); };
            }
            if (!backgroundWorkerFullLoad.IsBusy)
            {
                isGameLoadingComplete = false;
                formMain.locationToolStripComboBox.Enabled = isGameLoadingComplete;
                formMain.olvList.ClearObjects();
                backgroundWorkerFullLoad.RunWorkerAsync();
            }
        }

        public static string ReadableFileSize(double fileSize)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (fileSize >= 1024 && order < sizes.Length - 1)
            {
                order++;
                fileSize = fileSize / 1024;
            }

            // Adjust the format string to your preferences. For example "{0:0.#}{1}" would show a
            // single decimal place, and no space.
            return String.Format("{0:0.##} {1}", fileSize, sizes[order]);
        }

        public static byte[] ReadXciCert(string filePath)
        {
            byte[] array = new byte[512];

            if (File.Exists(filePath))
            {
                Log($"ReadXciCert {filePath}");
                FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                
                fileStream.Position = 28672L;
                fileStream.Read(array, 0, 512);
                fileStream.Close();
            }
            return array;
        }

        public static XciItem RefreshGame(XciItem xci, bool force = false)
        {
            Log($"RefreshGame: {xci.gameName}");
            if (force || !IsXciInfoValid(xci))
            {
                if (File.Exists(xci.xciFilePath))
                    xci = GetXciInfo(xci.xciFilePath, xci.xciLocation);
            }

            xci.isGameOnSd = (FindXciByIdentifer(xci.uniqueId, xciOnSd) != null);
            xci.isGameOnPc = (FindXciByIdentifer(xci.uniqueId, xciOnPc) != null);
            xci.keepInCache = true;

            //reset any file actions that would have happened with this xci
            xci.fileAction = new FileHelper.FileStruct();

            XciItem oldXci = FindXciByIdentifer(xci.uniqueId);
            lock (cacheListLock)
            {
                xciCache.Remove(oldXci);
                xciCache.Add(xci);
            }
            return xci;
        }

        public static void RefreshList()
        {
            Log($"RefreshList");
            if (!XciHelper.isGameLoadingComplete)
                return;

            formMain.locationToolStripComboBox.Enabled = false;

            formMain.olvList.ClearObjects();

            switch (Settings.config.defaultView)
            {
                case XciLocation.PC:
                    lock (pcListLock)
                        formMain.olvList.AddObjects(xciOnPc);

                    break;

                case XciLocation.SD:
                    lock (sdListLock)
                        formMain.olvList.AddObjects(xciOnSd);

                    break;
            }

            formMain.locationToolStripComboBox.Enabled = true;
        }

        public static void RefreshXciInBackground(XciItem xci)
        {
            Log($"RefreshXciInBackground {xci.gameName}");
            isGameLoadingComplete = false;
            formMain.locationToolStripComboBox.Enabled = isGameLoadingComplete;

            lock (refreshListLock)
                xciToRefresh.Add(xci);

            if (backgroundWorkerSingleLoad == null)
            {
                backgroundWorkerSingleLoad = new BackgroundWorker();

                backgroundWorkerSingleLoad.RunWorkerCompleted += delegate
                {
                    formMain.locationToolStripComboBox.Enabled = isGameLoadingComplete;

                    lock (refreshListLock)
                    {
                        XciItem xciRefresh = xciToRefresh.First();
                        XciItem oldXci;

                        oldXci = FindXciByIdentifer(xciRefresh.uniqueId, xciCache);

                        if (xciRefresh.xciLocation == XciLocation.PC)
                        {
                            oldXci = FindXciByIdentifer(xciRefresh.uniqueId, xciOnPc);
                            xciOnPc.Remove(oldXci);
                            xciOnPc.Add(xciRefresh);
                        }
                        else
                        {
                            oldXci = FindXciByIdentifer(xciRefresh.uniqueId, xciOnSd);
                            xciOnSd.Remove(oldXci);
                            xciOnSd.Add(xciRefresh);
                        }
                        
                        if (xciRefresh.xciLocation == Settings.config.defaultView)
                        {
                            formMain.olvList.RemoveObject(oldXci);
                            oldXci = xciRefresh;
                            formMain.olvList.AddObject(oldXci);
                        }

                        formMain.UpdateToolStripLabel($"Refreshed {xciRefresh.gameName}");
                        xciToRefresh.Remove(xciRefresh);
                    }

                    if (xciToRefresh.Count > 0)
                    {
                        backgroundWorkerSingleLoad.RunWorkerAsync();
                    }
                    else
                    {
                        SaveXciCache();
                        isGameLoadingComplete = true;
                        formMain.locationToolStripComboBox.Enabled = isGameLoadingComplete;
                    }
                };

                backgroundWorkerSingleLoad.DoWork += delegate
                {
                    XciItem xciRefresh;
                    lock (refreshListLock)
                    {
                        xciRefresh = RefreshGame(xciToRefresh.First(), true);
                        xciToRefresh[0] = xciRefresh;
                    }
                };
            }
            if (!backgroundWorkerSingleLoad.IsBusy)
            {
                backgroundWorkerSingleLoad.RunWorkerAsync();
            }
        }

        public static void RemoveXciFromList(XciItem xci, List<XciItem> xciList, bool fromOlvAlso = false)
        {
            try
            {
                if (fromOlvAlso)
                    if (xci.xciLocation == Settings.config.defaultView)
                        formMain.olvList.RemoveObject(xci);

                xciList.Remove(xci);
            }
            catch { }
        }

        public static void SaveXciCache(string fileName = "", List<XciItem> cache = null)
        {
            if (String.IsNullOrWhiteSpace(fileName))
                fileName = Settings.cacheFileName;

            Log($"SaveXciCache {fileName}");

            lock (cacheListLock)
            {
                if (cache == null)
                    cache = XciHelper.xciCache;

                if (cache == null || cache.Count == 0)
                    return;

                int length = cache.Count - 1;
                for (int i = length; i > 0; i--)
                {
                    if (!cache[i].keepInCache)
                    {
                        Log($"CacheCleanup: {cache[i].gameName}");
                        cache.RemoveAt(i);
                    }
                }

                File.WriteAllText(fileName, JsonConvert.SerializeObject(cache, Formatting.Indented));
            }
        }

        public static void ShowXciCert(XciItem xci)
        {
            XCI_Explorer.CertForm certForm;

            if (xci.gameCert == null)
                certForm = new XCI_Explorer.CertForm(xci.gameCert, xci.gameName);
            else
                certForm = new XCI_Explorer.CertForm(xci.xciFilePath, xci.gameName);

            certForm.Show();
        }

        public static void ShowXciExplorer(string filePath)
        {
            XCI_Explorer.MainForm mainForm = new XCI_Explorer.MainForm(true);
            mainForm.SGM_ProcessFile(filePath);
        }

        public static bool TrimXci(XciItem xci)
        {
            if (!File.Exists(xci.xciFilePath))
                return false;

            //maybe check this for errors? Maybe copy the file first, then do it on the copy? Needs tested.
            try
            {
                FileStream fileStream = new FileStream(xci.xciFilePath, FileMode.Open, FileAccess.Write);
                fileStream.SetLength((long)xci.gameUsedSize);
                fileStream.Close();
            }
            catch
            {
                return false;
            }

            return true;
        }

        public static void UpdateXci(XciItem xci)
        {


            Log($"UpdateXci: {xci.gameName}");

            XciItem xciTempSource = new XciItem();
            XciItem xciTempDest = new XciItem();

            Debug.Assert(xci.fileAction.actionCompleted);

            if (xci.fileAction.action == FileHelper.FileAction.None)
                return;

            if (xci.fileAction.source == XciLocation.SD)
            {
                xciTempSource = FindXciByIdentifer(xci.uniqueId, xciOnSd);
                xciTempDest = FindXciByIdentifer(xci.uniqueId, xciOnPc);
            }
            else
            {
                xciTempSource = FindXciByIdentifer(xci.uniqueId, xciOnPc);
                xciTempDest = FindXciByIdentifer(xci.uniqueId, xciOnSd);
            }

            switch (xci.fileAction.action)
            {
                case FileHelper.FileAction.None:
                    break;

                case FileHelper.FileAction.Copy:
                    xciTempDest = Clone(xci);
                    xciTempDest.xciFilePath = xci.fileAction.destinationPath;
                    xciTempDest.xciLocation = xci.fileAction.destination;
                    xciTempDest = RefreshGame(xciTempDest, true);

                    xciTempSource.isGameOnPc = true;
                    xciTempSource.isGameOnSd = true;
                    xciTempDest.isGameOnPc = true;
                    xciTempDest.isGameOnSd = true;

                    if (xci.fileAction.destination == XciLocation.PC)
                        xciOnPc.Add(xciTempDest);
                    else
                        xciOnSd.Add(xciTempDest);

                    xciTempDest.fileAction = new FileHelper.FileStruct();
                    xciTempSource.fileAction = new FileHelper.FileStruct();

                    if (Settings.config.defaultView == xciTempDest.xciLocation)
                        formMain.olvList.AddObject(xciTempDest);
                    else
                        formMain.olvList.RefreshObject(xciTempSource);

                    break;

                case FileHelper.FileAction.Move:

                    if (!File.Exists(xci.fileAction.destinationPath))
                        return;
                    if (File.Exists(xci.fileAction.sourcePath))
                        File.Delete(xci.fileAction.sourcePath);

                    xciTempDest = Clone(xci);
                    xciTempDest.fileAction = new FileHelper.FileStruct();
                    xciTempDest.xciFilePath = xci.fileAction.destinationPath;
                    xciTempDest.xciLocation = xci.fileAction.destination;
                    xciTempDest = RefreshGame(xciTempDest, true);

                    if (xci.fileAction.destination == XciLocation.PC)
                    {
                        xciTempDest.isGameOnPc = true;
                        xciTempDest.isGameOnSd = false;
                        xciOnPc.Add(xciTempDest);
                        xciOnSd.Remove(xciTempSource);
                    }
                    else
                    {
                        xciTempDest.isGameOnPc = false;
                        xciTempDest.isGameOnSd = true;
                        xciOnSd.Add(xciTempDest);
                        xciOnPc.Remove(xciTempSource);
                    }

                    if (Settings.config.defaultView == xciTempDest.xciLocation)
                        formMain.olvList.AddObject(xciTempDest);
                    else
                        formMain.olvList.RemoveObject(xciTempSource);

                    break;

                case FileHelper.FileAction.Delete:
                    //Check if file exists any longer. May not be necessary if we just refresh the xci lists
                    if (!File.Exists(xci.xciFilePath))
                    {
                        if (xci.xciLocation == XciLocation.PC)
                        {
                            if (xciTempSource != null)
                                xciOnPc.Remove(xciTempSource);
                            if (xciTempDest != null)
                                xciTempDest.isGameOnPc = false;
                        }
                        else
                        {
                            xciOnSd.Remove(xciTempSource);
                            xciTempDest.isGameOnSd = false;
                        }

                        if (Settings.config.defaultView == xci.xciLocation)
                            formMain.olvList.RemoveObject(xciTempSource);
                        else
                            formMain.olvList.RefreshObject(xciTempDest);
                    }
                    break;

                case FileHelper.FileAction.CompletelyDelete:
                    if (!File.Exists(xci.xciFilePath))
                    {
                        if (Settings.config.defaultView == xci.xciLocation)
                            formMain.olvList.RemoveObject(xciTempSource);

                        if (xci.xciLocation == XciLocation.PC)
                            xciOnPc.Remove(xciTempSource);
                        else
                            xciOnSd.Remove(xciTempSource);
                    }
                    break;

                case FileHelper.FileAction.Trim:
                    //check if the file size changed, and if it did, get the XCI Info again
                    xciTempSource.gameSize = (double)new FileInfo(xci.xciFilePath).Length;
                    xciTempSource.isXciTrimmed = (xciTempSource.gameSize == xci.gameUsedSize);

                    xciTempSource.fileAction = new FileHelper.FileStruct();

                    if (Settings.config.defaultView == xci.xciLocation)
                        formMain.olvList.UpdateObject(xciTempSource);

                    break;

                default:
                    break;
            }
        }

        public static bool UpdateXciCache(XciItem xci)
        {
            Log($"UpdateXciCache: {xci.gameName}");

            lock (cacheListLock)
            {
                XciItem xciTemp = FindXciByIdentifer(xci.uniqueId);

                if (!IsXciInfoValid(xciTemp))
                {
                    if (xciTemp == null)
                    {
                        xciCache.Add(xci);
                        return true;
                    }
                }
            }
            return false;
        }

        public static string sha256(string randomString)
        {
            var crypt = new System.Security.Cryptography.SHA256Managed();
            var hash = new System.Text.StringBuilder();
            byte[] crypto = crypt.ComputeHash(Encoding.UTF8.GetBytes(randomString));
            foreach (byte theByte in crypto)
            {
                hash.Append(theByte.ToString("x2"));
            }
            return hash.ToString();
        }

        public static byte[] Compress(byte[] raw)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(memory,
                    CompressionMode.Compress, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }
                return memory.ToArray();
            }
        }

        public static byte[] Decompress(byte[] gzip)
        {
            // Create a GZIP stream with decompression mode.
            // ... Then create a buffer and write into while reading from the GZIP stream.
            using (GZipStream stream = new GZipStream(new MemoryStream(gzip),
                CompressionMode.Decompress))
            {
                const int size = 4096;
                byte[] buffer = new byte[size];
                using (MemoryStream memory = new MemoryStream())
                {
                    int count = 0;
                    do
                    {
                        count = stream.Read(buffer, 0, size);
                        if (count > 0)
                        {
                            memory.Write(buffer, 0, count);
                        }
                    }
                    while (count > 0);
                    return memory.ToArray();
                }
            }
        }
    }
}