using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SoulsFormats;
using System.Linq;
using System.Xml;

namespace Yabber
{
    static class YBUtil
    {
        public static string GetExeLocation()
        {
            return Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
        }

        public enum GameType
        {
            BB,
            DES,
            DS1,
            DS1R,
            DS2,
            DS2S,
            DS3,
            ER,
            SDT
        }

        public static Dictionary<GameType, string> GameNames = new Dictionary<GameType, string>()
        {
            { GameType.BB, "BB" },
            { GameType.DES, "DES" },
            { GameType.DS1, "DS1" },
            { GameType.DS1R, "DS1R" },
            { GameType.DS2, "DS2" },
            { GameType.DS2S, "DS2S" },
            { GameType.DS3, "DS3" },
            { GameType.ER, "ER" },
            { GameType.SDT, "SDT" }
        };

        public static GameType DetermineParamdexGame(string path)
        {
            GameType? gameNullable = null;

            // Determine what kind of PARAM we're dealing with here
            var yabberXmlPath = $@"{path}\_yabber-bnd4.xml";
            if (File.Exists(yabberXmlPath))
            {
                XmlDocument xml = new XmlDocument();
                xml.Load(yabberXmlPath);

                string filename = xml.SelectSingleNode("bnd4/filename").InnerText;
                if (filename == "regulation.bin")
                {
                    // We are loading ELDEN RING param
                    gameNullable = GameType.ER;
                }
            }

            if (gameNullable != null)
            {
                Console.WriteLine($"Determined game for Paramdex: {GameNames[gameNullable.Value]}");
            }
            else
            {
                Console.WriteLine("Could not determine param game version.");
                Console.WriteLine("Please input a game from the following list:");
                Console.WriteLine(String.Join(", ", YBUtil.GameNames.Values));
                Console.Write($"Game: ");
                string input = Console.ReadLine().ToUpper();
                var flippedDict = YBUtil.GameNames.ToDictionary(pair => pair.Value, pair => pair.Key);
                if (string.IsNullOrEmpty(input) || !flippedDict.ContainsKey(input))
                {
                    throw new Exception("Could not determine PARAM type.");
                }

                gameNullable = flippedDict[input];
            }

            return gameNullable.Value;
        }

        /// <summary>
        /// General forking madness around SoulsFormats and Paramdex means that the
        /// Paramdex repo and the DSMS Paramdex have diverged in DataVersions in paramdefs,
        /// which for most end users should not be a big deal.
        /// This function excludes the SF dataversion check.
        /// </summary>
        public static bool ApplyParamdefLessCarefully(this PARAM param, PARAMDEF paramdef)
        {
            if (param.ParamType == paramdef.ParamType && (param.DetectedSize == -1 || param.DetectedSize == paramdef.GetRowSize()))
            {
                param.ApplyParamdef(paramdef);
                return true;
            }
            return false;
        }

        private static readonly Regex DriveRx = new Regex(@"^(\w\:\\)(.+)$");
        private static readonly Regex TraversalRx = new Regex(@"^([(..)\\\/]+)(.+)?$");
        private static readonly Regex SlashRx = new Regex(@"^(\\+)(.+)$");


        /// <summary>
        /// Finds common path prefix in a list of strings.
        /// </summary>
        public static string FindCommonRootPath(IEnumerable<string> paths)
        {
            string root = "";

            var rootPath = new string(
                paths.First().Substring(0, paths.Min(s => s.Length))
                    .TakeWhile((c, i) => paths.All(s => s[i] == c)).ToArray());

            // For safety, truncate this shared string down to the last slash/backslash.
            var rootPathIndex = Math.Max(rootPath.LastIndexOf('\\'), rootPath.LastIndexOf('/'));

            if (rootPath != "" && rootPathIndex != -1)
            {
                root = rootPath.Substring(0, rootPathIndex);
            }

            return root;
        }

        /// <summary>
        /// Removes common network path roots if present.
        /// </summary>
        public static string UnrootBNDPath(string path, string root)
        {
            path = path.Substring(root.Length);

            Match drive = DriveRx.Match(path);
            if (drive.Success)
            {
                path = drive.Groups[2].Value;
            }

            Match traversal = TraversalRx.Match(path);
            if (traversal.Success)
            {
                path = traversal.Groups[2].Value;
            }

            if (path.Contains("..\\") || path.Contains("../"))
                throw new InvalidDataException(
                    $"the path {path} contains invalid data, attempting to extract to a different folder. Please report this bnd to Nordgaren.");
            return RemoveLeadingBackslashes(path);
        }

        private static string RemoveLeadingBackslashes(string path)
        {
            Match slash = SlashRx.Match(path);
            if (slash.Success)
            {
                path = slash.Groups[2].Value;
            }

            return path;
        }

        public static void Backup(string path)
        {
            if (File.Exists(path) && !File.Exists(path + ".bak"))
                File.Move(path, path + ".bak");
        }

        private static byte[] ds2RegulationKey =
            { 0x40, 0x17, 0x81, 0x30, 0xDF, 0x0A, 0x94, 0x54, 0x33, 0x09, 0xE1, 0x71, 0xEC, 0xBF, 0x25, 0x4C };

        /// <summary>
        /// Decrypts and unpacks DS2's regulation BND4 from the specified path.
        /// </summary>
        public static BND4 DecryptDS2Regulation(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] iv = new byte[16];
            iv[0] = 0x80;
            Array.Copy(bytes, 0, iv, 1, 11);
            iv[15] = 1;
            byte[] input = new byte[bytes.Length - 32];
            Array.Copy(bytes, 32, input, 0, bytes.Length - 32);
            using (var ms = new MemoryStream(input))
            {
                byte[] decrypted = CryptographyUtil.DecryptAesCtr(ms, ds2RegulationKey, iv);
                return BND4.Read(decrypted);
            }
        }

        static (string, string)[] _pathValueTuple = new (string, string)[]
        {
            (@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath"),
            (@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath"),
            (@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"),
            (@"HKEY_CURRENT_USER\SOFTWARE\Wow6432Node\Valve\Steam", "SteamPath"),
        };

        public static string TryGetGameInstallLocation(string gamePath)
        {
            if (!gamePath.StartsWith("\\") && !gamePath.StartsWith("/"))
                return null;

            string steamPath = GetSteamInstallPath();

            if (string.IsNullOrWhiteSpace(steamPath))
                return null;

            string[] libraryFolders = File.ReadAllLines($@"{steamPath}/SteamApps/libraryfolders.vdf");
            char[] seperator = { '\t' };

            foreach (string line in libraryFolders)
            {
                if (!line.Contains("\"path\""))
                    continue;

                string[] split = line.Split(seperator, StringSplitOptions.RemoveEmptyEntries);
                string libpath = split.FirstOrDefault(x => x.ToLower().Contains("steam")).Replace("\"", "")
                    .Replace("\\\\", "\\");
                string libraryPath = libpath + gamePath;

                if (File.Exists(libraryPath))
                    return libraryPath.Replace("\\\\", "\\");
            }

            return null;
        }

        public static string GetSteamInstallPath()
        {
            string installPath = null;

            foreach ((string Path, string Value) pathValueTuple in _pathValueTuple)
            {
                string registryKey = pathValueTuple.Path;
                installPath = (string)Registry.GetValue(registryKey, pathValueTuple.Value, null);

                if (installPath != null)
                    break;
            }

            return installPath;
        }

        private static string[] OodleGames =
        {
            "Sekiro",
            "ELDEN RING",
        };

        public static string GetOodlePath()
        {
            foreach (string game in OodleGames)
            {
                string path = TryGetGameInstallLocation($"\\steamapps\\common\\{game}\\Game\\oo2core_6_win64.dll");
                if (path != null)
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Helpers for delimited string, with support for escaping the delimiter
        /// character.
        /// https://coding.abel.nu/2016/06/string-split-and-join-with-escaping/
        /// </summary>
        public static class DelimitedString
        {
            const string DelimiterString = ",";
            const char DelimiterChar = ',';

            // Use a single / as escape char, avoid \ as that would require
            // all escape chars to be escaped in the source code...
            const char EscapeChar = '/';
            const string EscapeString = "/";

            /// <summary>
            /// Join strings with a delimiter and escape any occurence of the
            /// delimiter and the escape character in the string.
            /// </summary>
            /// <param name="strings">Strings to join</param>
            /// <returns>Joined string</returns>
            public static string Join(params string[] strings)
            {
                return string.Join(
                    DelimiterString,
                    strings.Select(
                        s => s
                            .Replace(EscapeString, EscapeString + EscapeString)
                            .Replace(DelimiterString, EscapeString + DelimiterString)));
            }
            public static string Join(IEnumerable<string> strings)
            {
                return Join(strings.ToArray());
            }

            /// <summary>
            /// Split strings delimited strings, respecting if the delimiter
            /// characters is escaped.
            /// </summary>
            /// <param name="source">Joined string from <see cref="Join(string[])"/></param>
            /// <returns>Unescaped, split strings</returns>
            public static string[] Split(string source)
            {
                var result = new List<string>();

                int segmentStart = 0;
                for (int i = 0; i < source.Length; i++)
                {
                    bool readEscapeChar = false;
                    if (source[i] == EscapeChar)
                    {
                        readEscapeChar = true;
                        i++;
                    }

                    if (!readEscapeChar && source[i] == DelimiterChar)
                    {
                        result.Add(UnEscapeString(
                            source.Substring(segmentStart, i - segmentStart)));
                        segmentStart = i + 1;
                    }

                    if (i == source.Length - 1)
                    {
                        result.Add(UnEscapeString(source.Substring(segmentStart)));
                    }
                }

                return result.ToArray();
            }

            static string UnEscapeString(string src)
            {
                return src.Replace(EscapeString + DelimiterString, DelimiterString)
                    .Replace(EscapeString + EscapeString, EscapeString);
            }
        }

    }

}