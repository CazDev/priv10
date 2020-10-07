﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;

namespace MiscHelpers
{
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    public static class MiscFunc
    {
        public static UInt64 GetUTCTime()
        {
            return (UInt64)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalSeconds;
        }

        public static UInt64 GetUTCTimeMs()
        {
            return (UInt64)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
        }


        public static UInt64 DateTime2Ms(DateTime dateTime)
        {
            return (UInt64)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
        }

        static public List<string> EnumAllFiles(string sourcePath)
        {
            List<string> files = new List<string>();

            foreach (string fileName in Directory.GetFiles(sourcePath))
                files.Add(fileName);

            foreach (string dirName in Directory.GetDirectories(sourcePath))
            {
                if ((new DirectoryInfo(dirName).Attributes & FileAttributes.ReparsePoint) != 0)
                    continue; // skip junctions

                files.AddRange(EnumAllFiles(dirName));
            }

            return files;
        }

        public static TValue GetOrCreate<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key)
        where TValue : new()
        {
            TValue val;

            if (!dict.TryGetValue(key, out val))
            {
                val = new TValue();
                dict.Add(key, val);
            }

            return val;
        }

        public static bool Exec(string cmd, string args, bool hidden = true)
        {
            try
            {
                Process process = new Process();
                process.StartInfo.UseShellExecute = false;
                if (hidden)
                {
                    process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    process.StartInfo.CreateNoWindow = true;
                }
                process.StartInfo.FileName = cmd;
                process.StartInfo.Arguments = args;
                process.StartInfo.Verb = "runas"; // run as admin
                process.Start();
                process.WaitForExit();
                return true;
            }
            catch (Exception err)
            {
                AppLog.Exception(err);
            }
            return false;
        }

        public static UInt64 GetCurTick()
        {
            return (UInt64)DateTime.Now.Ticks / 10000; // ticks in ms
        }

        /*internal static void ActiveSleep(int ms)
        {
            DateTime until = DateTime.Now.Add(new TimeSpan(0, 0, 0, 0, ms));
            while (until >= DateTime.Now)
                System.Windows.Forms.Application.DoEvents();
        }*/

        [DllImport("User32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("Kernel32.dll")]
        private static extern uint GetLastError();

        public static uint GetIdleTime() // in seconds
        {
            LASTINPUTINFO lastInPut = new LASTINPUTINFO();
            lastInPut.cbSize = (uint)Marshal.SizeOf(lastInPut);
            if (!GetLastInputInfo(ref lastInPut))
            {
                throw new Exception(GetLastError().ToString());
            }
            return ((uint)Environment.TickCount - lastInPut.dwTime) / 1000;
        }

        public static int parseInt(string str, int def = 0)
        {
            int ret;
            if (int.TryParse(str, out ret))
                return ret;
            return def;
        }

        public static double parseDouble(string str, double def = 0)
        {
            double ret;
            if (double.TryParse(str, out ret))
                return ret;
            return def;
        }

        public static bool? parseBool(string str, bool? def = false)
        {
            if (str.Length == 0)
                return def;

            bool ret;
            if (bool.TryParse(str, out ret))
                return ret;
            return def;
        }

        public static string NtOsKrnlPath = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\ntoskrnl.exe");

        public static string Shell32Path = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\shell32.dll");

        public static string parsePath(string path)
        {
            try
            {
                if (path.Contains(@"\device\mup\"))
                    return @"\" + path.Substring(11, path.Length - 11);
                string[] strArray = path.Split(new char[1] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                string vol = @"\" + strArray[0] + @"\" + strArray[1];
                path = path.Replace(vol, GetDriveLetter(vol));
                if (path.Contains('~'))
                    path = Path.GetFullPath(path);
                return path;
            }
            catch (Exception err)
            {
                AppLog.Exception(err);
            }
            return "";
        }

        [DllImport("kernel32.dll")]
        public static extern UInt64 GetTickCount64();

        [DllImport("kernel32.dll")]
        public static extern uint QueryDosDevice(string lpDeviceName, [In, Out] char[] lpTargetPath, int ucchMax);

        private static Dictionary<string, Tuple<string, UInt64>> DriveLetterCache = new Dictionary<string, Tuple<string, UInt64>>();
        private static ReaderWriterLockSlim DriveLetterCacheLock = new ReaderWriterLockSlim();

        private static string GetDriveLetter(string longPath)
        {
            Tuple<string, UInt64> temp;
            DriveLetterCacheLock.EnterReadLock();
            if (DriveLetterCache.TryGetValue(longPath.ToLower(), out temp))
            {
                if (temp.Item2 > GetTickCount64())
                {
                    DriveLetterCacheLock.ExitReadLock();
                    return temp.Item1;
                }
                DriveLetterCache.Remove(longPath.ToLower());
            }
            DriveLetterCacheLock.ExitReadLock();

            // ToDo: build a cache on WM_DEVICECHANGE

            string ret = null;
            char[] lpTargetPath = new char[260 + 1];
            for (char ltr = 'A'; ltr <= 'Z'; ltr++)
            {
                uint size = QueryDosDevice(ltr + ":", lpTargetPath, 260);
                if (size > 0 && longPath.Equals(new String(lpTargetPath, 0, (int)size - 2), StringComparison.OrdinalIgnoreCase))
                {
                    ret = ltr + ":";
                    break;
                }
            }

            if (ret == null)
                return "?:";

            DriveLetterCacheLock.EnterWriteLock();
            if (DriveLetterCache.ContainsKey(longPath.ToLower()) == false)
                DriveLetterCache.Add(longPath.ToLower(), new Tuple<string, UInt64>(ret, GetTickCount64() + 1 * 60 * 1000)); // cahce values for 1 minutes
            DriveLetterCacheLock.ExitWriteLock();
            return ret;
        }

        public static string Sys32Path = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\");
        public static string SysWOWPath = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\SysWOW64\");

        public static bool IsWindowsBinary(string path)
        {
            if (path == null || path.Length == 0)
                return false;

            if (path.IndexOf(Sys32Path, StringComparison.OrdinalIgnoreCase) == 0 || path.IndexOf(SysWOWPath, StringComparison.OrdinalIgnoreCase) == 0)
            {
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);

                    bool isMS = info.CompanyName == "Microsoft Corporation";

                    return isMS;
                }
                catch { }
            }

            return false;
        }

        public static string GetExeDescription(string appPath)
        {
            string descr = null;
            if (File.Exists(appPath))
            {
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(appPath);
                    descr = info?.FileDescription;
                }
                catch { }
            }
            return (descr != null && descr.Length > 0) ? descr : "";
        }


        /*public static string GetServiceNameByPID(int pid)
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT Name FROM Win32_Service WHERE ProcessId = " + pid);
            foreach (ManagementObject queryObj in searcher.Get())
            {
                string ret = queryObj["Name"].ToString();
                return ret;
            }
            return null;
        }*/

        public static bool StrCmp(string value, string test)
        {
            if (value == null)
                return test == null;
            if (test == null)
                return false;
            return value.Equals(test, StringComparison.OrdinalIgnoreCase);
        }

        /*public static string GetServiceName(string name)
        {
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT DisplayName FROM Win32_Service WHERE Name = \"" + name + "\"");
            foreach (ManagementObject queryObj in searcher.Get())
            {
                return queryObj["DisplayName"].ToString();
            }
            return "";
        }*/

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        public static string GetResourceStr(string resourcePath)
        {
            StringBuilder buffer = new StringBuilder(4096);
            int result = SHLoadIndirectString(resourcePath, buffer, buffer.Capacity, IntPtr.Zero);
            if (result == 0)
                return buffer.ToString();
            return resourcePath;
        }

        public static string GetResourceStr(string path, string resID)
        {
            return GetResourceStr("@{" + path + "? " + resID + "}");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static public string GetCurrentMethod()
        {
            try
            {
                var st = new StackTrace();
                var sf = st.GetFrame(1);
                return sf.GetMethod().Name;
            }
            catch
            {
                return "Unknown";
            }
        }

        static public bool IsOnScreen(Rectangle formRectangle)
        {
            Screen[] screens = Screen.AllScreens;
            foreach (Screen screen in screens)
            {
                if (screen.WorkingArea.Contains(formRectangle))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsValidRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;

            try
            {
                Regex.Match("", pattern);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return true;
        }

        public static bool IsEqual<T>(T L, T R)
        {
            if (L == null)
                return (R == null);
            return L.Equals(R);
        }

        public static T Max<T>(T x, T y)
        {
            return (Comparer<T>.Default.Compare(x, y) > 0) ? x : y;
        }

        public static T Min<T>(T x, T y)
        {
            return (Comparer<T>.Default.Compare(x, y) < 0) ? x : y;
        }

        public static class ClipboardNative
        {
            [DllImport("user32.dll")]
            private static extern bool OpenClipboard(IntPtr hWndNewOwner);

            [DllImport("user32.dll")]
            private static extern bool CloseClipboard();

            [DllImport("user32.dll")]
            private static extern bool SetClipboardData(uint uFormat, IntPtr data);

            private const uint CF_UNICODETEXT = 13;

            public static bool CopyTextToClipboard(string text)
            {
                if (!OpenClipboard(IntPtr.Zero))
                {
                    return false;
                }

                var global = Marshal.StringToHGlobalUni(text);

                SetClipboardData(CF_UNICODETEXT, global);
                CloseClipboard();

                //-------------------------------------------
                // Not sure, but it looks like we do not need 
                // to free HGLOBAL because Clipboard is now 
                // responsible for the copied data. (?)
                //
                // Otherwise the second call will crash
                // the app with a Win32 exception 
                // inside OpenClipboard() function
                //-------------------------------------------
                // Marshal.FreeHGlobal(global);

                return true;
            }
        }
    }
}