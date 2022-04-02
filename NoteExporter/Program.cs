using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using Serilog;

namespace NoteExporter {
    internal class Program {
        private const string Usage = "Usage: NoteExporter.exe <USTX file>";

        public static int Main(string[] args) {
            if (args.Length != 1) {
                Console.WriteLine(Usage);
                return 1;
            }
            var path = args[0];
            if (Path.GetExtension(path) != ".ustx" || !File.Exists(path)) {
                Console.WriteLine(Usage);
                return 1;
            }

            Init();
            var project = LoadProject(path);
            var renderResults = Render(project);
            var exportDir = GetExportDirectory(path);
            WriteResults(renderResults, exportDir);
            return 0;
        }

        private static void Init() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Directory.CreateDirectory(PathManager.Inst.CachePath);
            Resamplers.Search();
            DocManager.Inst.Initialize();
        }

        private static UProject LoadProject(string path) {
            return Ustx.Load(path);
        }

        private static List<NoteResult> Render(UProject project) {
            var phrases = GetPhrases(project);
            var resamplerItems = GetResamplerItems(phrases);
            var results = new List<NoteResult>();
            foreach (var item in resamplerItems) {
                VoicebankFiles.CopySourceTemp(item.inputFile, item.inputTemp);
                Console.WriteLine($"Rendering position {item.phone.position}: {item.phone.phoneme}");
                var file = item.resampler.DoResamplerReturnsFile(item, Log.Logger);
                if (!File.Exists(item.outputFile)) {
                    throw new InvalidDataException($"{item.resampler.Name} failed to resample \"{item.phone.phoneme}\"");
                }
                VoicebankFiles.CopyBackMetaFiles(item.inputFile, item.inputTemp);
                results.Add(new NoteResult(item.phone, file));
            }
            return results;
        }

        private static IEnumerable<RenderPhrase> GetPhrases(UProject project) {
            return project.tracks
                .SelectMany(track => GetPhrasesInTrack(project, track));
        }

        private static IEnumerable<RenderPhrase> GetPhrasesInTrack(UProject project, UTrack track) {
            return project.parts
                .Where(part => part.trackNo == track.TrackNo)
                .Where(part => part is UVoicePart)
                .Select(part => part as UVoicePart)
                .SelectMany(part => RenderPhrase.FromPart(project, project.tracks[part.trackNo], part));
        }

        private static List<ResamplerItem> GetResamplerItems(IEnumerable<RenderPhrase> phrases) {
            var resamplerItems = new List<ResamplerItem>();
            foreach (var phrase in phrases) {
                foreach (var phone in phrase.phones) {
                    resamplerItems.Add(new ResamplerItem(phrase, phone));
                }
            }
            return resamplerItems;
        }

        private static string GetExportDirectory(string input) {
            var inputFull = Path.GetFullPath(input);
            var inputDirectory = Path.GetDirectoryName(inputFull);
            return Path.Join(inputDirectory, "Export");
        }

        private static void WriteResults(List<NoteResult> results, string directory) {
            Directory.CreateDirectory(directory);
            foreach (var result in results) {
                var exportPath = Path.Join(directory, result.ToString());
                try {
                    File.Copy(result.FilePath, exportPath, true);
                } catch (SystemException err) {
                    Console.Error.WriteLine($"Error writing {exportPath}: {err.Message}");
                }
            }
        }
    }

    internal class NoteResult {
        public int PreUtterTick { get; private set; }
        public int PositionTick { get; private set; }
        public string Phoneme { get;  private set; }
        public string FilePath { get;  private set; }

        public NoteResult(RenderPhone phone, string file) {
            PreUtterTick = phone.leading;
            PositionTick = phone.position;
            Phoneme = phone.phoneme;
            FilePath = file;
        }

        public override string ToString()
            => string.Format("{0:D6}_{1:D6}_{2}.wav", PositionTick, PreUtterTick, Phoneme);
    }
}
