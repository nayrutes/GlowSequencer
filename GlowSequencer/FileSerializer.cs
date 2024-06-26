﻿using GlowSequencer.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace GlowSequencer
{
    public class FileSerializer
    {
        private const int TICKS_PER_SECOND = 100;
        public const string EXTENSION_PROJECT = ".gls";
        public const string EXTENSION_EXPORT = ".glo";

        public static Timeline LoadFromFile(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                {
                    System.Windows.MessageBox.Show("The file \"" + filename + "\" was not found!", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Exclamation);
                    return null;
                }

                XDocument doc;
                try
                {
                    using (GZipStream zipStream = new GZipStream(new FileStream(filename, FileMode.Open, FileAccess.Read), CompressionMode.Decompress))
                        doc = XDocument.Load(zipStream);
                }
                // fallback for old, uncompressed save files
                catch (InvalidDataException) { doc = XDocument.Load(filename); }

                try
                {
                    Timeline timeline = Timeline.FromXML(doc.Root.Element("timeline"), Path.GetDirectoryName(filename));
                    return timeline;
                }
                catch (Exception e)
                {
                    // Probably some old file format that we no longer support.
                    System.Windows.MessageBox.Show("An error occured while parsing \"" + filename + "\"!" + Environment.NewLine + Environment.NewLine + e.GetType().FullName + ": " + e.Message,
                        "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Exclamation);
                    return null;
                }
            }
            catch (IOException e)
            {
                System.Windows.MessageBox.Show("The file \"" + filename + "\" could not be opened!" + Environment.NewLine + e.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Exclamation);
                return null;
            }
            catch (UnauthorizedAccessException e)
            {
                System.Windows.MessageBox.Show("The file \"" + filename + "\" could not be opened!" + Environment.NewLine + e.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Exclamation);
                return null;
            }
            catch (XmlException e)
            {
                System.Windows.MessageBox.Show("The file \"" + filename + "\" has been corrupted!" + Environment.NewLine + e.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Exclamation);
                return null;
            }
        }

        public static bool SaveToFile(Timeline timeline, string filename, bool compressed)
        {
            XDocument doc = new XDocument();
            doc.Add(new XElement("sequence",
                new XElement("version", GetProgramVersion()),
                timeline.ToXML(Path.GetDirectoryName(filename))
            ));

            try
            {
                if (compressed)
                {
                    using (GZipStream zipStream = new GZipStream(new FileStream(filename, FileMode.Create, FileAccess.Write), CompressionMode.Compress))
                    {
                        doc.Save(zipStream);
                    }
                }
                else
                {
                    using (FileStream stream = new FileStream(filename, FileMode.Create, FileAccess.Write))
                    {
                        doc.Save(stream);
                    }
                }
                return true;
            }
            catch (IOException e)
            {
                System.Windows.MessageBox.Show("Could not save to file \"" + filename + "\"!" + Environment.NewLine + e.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Exclamation);
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                System.Windows.MessageBox.Show("Could not save to file \"" + filename + "\"!" + Environment.NewLine + e.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Exclamation);
                return false;
            }
        }


        public class PrimitiveBlock
        {
            private static int idGen = 10000;
            public readonly int id = ++idGen;

            public int startTime, endTime; // endTime is exclusive
            public GloColor startColor, endColor;

            public PrimitiveBlock(float tStart, float tEnd, GloColor colStart, GloColor colEnd)
            {
                startTime = ToTicks(tStart);
                endTime = ToTicks(tEnd);
                startColor = colStart;
                endColor = colEnd;
            }

            public GloColor ColorAt(int tick)
            {
                if (startColor == endColor)
                    return startColor;

                return GloColor.Blend(startColor, endColor, (tick - startTime) / (double)(endTime - startTime));
            }

            public static int ToTicks(float time)
            {
                return (int)Math.Round(time * TICKS_PER_SECOND);
            }

            public override string ToString()
            {
                return "" + id;
            }
        }

        private struct Sample
        {
            public int ticks;
            public GloColor colBefore;
            public GloColor colAfter;
            public PrimitiveBlock blockBefore;
            public PrimitiveBlock blockAfter;
        }

        public static string SanitizeString(string str)
        {
            return Path.GetInvalidFileNameChars().Aggregate(str, (current, c) => current.Replace(c.ToString(), "")).Replace(' ', '_');
        }

        public static bool ExportGloFiles(Timeline timeline, string filenameBase, string filenameSuffix, float startTime)
        {
            foreach (var track in timeline.Tracks)
            {
                string sanitizedTrackName = SanitizeString(track.Label);
                string file = filenameBase + sanitizedTrackName + filenameSuffix;
                ExportTrackToFile(track, file, startTime);
            }


            // old algorithm for comparison
            //foreach (var track in timeline.Tracks)
            //{
            //    GloCommandContainer container = new GloCommandContainer(null, "END");
            //    try
            //    {
            //        var ctx = new GloSequenceContext(track, container);
            //        ctx.Append(track.Blocks);
            //        ctx.Postprocess();
            //    }
            //    catch (InvalidOperationException e)
            //    {
            //        // TO_DO showing the error as a message box from here breaks layer architecture
            //        System.Windows.MessageBox.Show("Error while exporting track '" + track.Label + "': " + e.Message);
            //        return false;
            //    }

            //    // write to file
            //    string sanitizedTrackName = System.IO.Path.GetInvalidFileNameChars().Aggregate(track.Label, (current, c) => current.Replace(c.ToString(), "")).Replace(' ', '_');
            //    string file = filenameBase + sanitizedTrackName + "_old" + filenameSuffix;
            //    WriteCommands(container, file);
            //}

            return true;
        }

        public static GloCommandContainer ExportTrackToContainer(
            Track track,
            float startTime,
            ColorTransformMode colorMode = ColorTransformMode.None
        )
        {
            // Algorithm "back-to-front rendering" := every block paints all affected samples with its data
            // Each sample stores "color up to this point" and "color from this point forward" along with the block that set the respective half.
            // After painting is complete, all samples that just pass through a single block are redundant.

            Sample[] samples = CollectSamples(track, startTime, colorMode);
            GloCommandContainer commandContainer = SamplesToCommands(samples);
            OptimizeCommands(commandContainer.Commands);
            return commandContainer;
        }

        public static void ExportTrackToFile(Track track, string filename, float startTime)
        {
            GloCommandContainer commandContainer = ExportTrackToContainer(track, startTime);
            WriteCommands(commandContainer, filename);
        }


        private static Sample[] CollectSamples(Track track, float exportStartTime, ColorTransformMode colorMode)
        {
            // IMPORTANT: this uses the Timeline.Blocks collection, NOT the Track.Blocks collection;
            // the latter is only a view, which does not preserve ordering, so multi-layer blocks are broken when using Track.Blocks;
            // Block.BakePrimitive(track) is responsible for filtering blocks based on tracks
            IEnumerable<Block> blocks = track.GetTimeline().Blocks;

            int firstTick = (int)Math.Round(exportStartTime * TICKS_PER_SECOND);
            if (firstTick > 0)
                // eliminate blocks that lie fully outside the export range
                blocks = blocks.Where(b => b.GetEndTime() >= exportStartTime);

            // ticksOffset is ignored in the main painting phase, but time values are shifted at the post-filter stage

            List<PrimitiveBlock> allBlocks = blocks.SelectMany(b => b.BakePrimitive(track)).ToList();
            TransformBlockColors(allBlocks, colorMode);

            int length = allBlocks.Max(b => (int?)b.endTime) ?? 0;
            var samples = new Sample[length + 1];
            for (int i = 0; i < samples.Length; i++)
                samples[i].ticks = i;

            foreach (var primBlock in allBlocks)
            {
                samples[primBlock.startTime].colAfter = primBlock.startColor;
                samples[primBlock.startTime].blockAfter = primBlock;

                for (int tick = primBlock.startTime + 1; tick < primBlock.endTime; tick++)
                {
                    samples[tick].colAfter = primBlock.ColorAt(tick);
                    samples[tick].colBefore = primBlock.ColorAt(tick);
                    samples[tick].blockBefore = primBlock;
                    samples[tick].blockAfter = primBlock;
                }

                samples[primBlock.endTime].colBefore = primBlock.endColor;
                samples[primBlock.endTime].blockBefore = primBlock;
            }

            //if (track.Label == "Track 02")
            //    File.WriteAllLines("D:\\debug_samples_pre.txt", samples.Select(s => s.ticks.ToString("000000") + ": " + s.colBefore.ToHexString() + " -> " + s.colAfter.ToHexString() + " ; " + s.blockBefore + " -> " + s.blockAfter));

            samples = samples
                // eliminate samples that lie before the time range
                .Where(s => s.ticks >= firstTick)
                // samples where the block does not change are redundant
                .Where(s => s.blockBefore != s.blockAfter)
                .ToArray();

            //if (track.Label == "Track 02")
            //    File.WriteAllLines("D:\\debug_samples_post.txt", samples.Select(s => s.ticks.ToString("000000") + ": " + s.colBefore.ToHexString() + " -> " + s.colAfter.ToHexString() + " ; " + s.blockBefore + " -> " + s.blockAfter));

            // adjust ticks for the ticksOffset
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i].ticks -= firstTick;
            }

            // make absolutely sure all colors are in [0..255] range
            // do NOT use foreach since it will copy the Sample structs ...
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i].colBefore.Normalize();
                samples[i].colAfter.Normalize();
            }

            return samples;
        }

        private static void TransformBlockColors(IList<PrimitiveBlock> primitives, ColorTransformMode colorMode)
        {
            foreach (var block in primitives)
            {
                block.startColor = GloColor.TransformToMode(block.startColor, colorMode);
                block.endColor = GloColor.TransformToMode(block.endColor, colorMode);
            }
        }

        private static GloCommandContainer SamplesToCommands(Sample[] samples)
        {
            GloCommandContainer container = new GloCommandContainer(null, "END");
            GloColor lastColor = GloColor.Black;
            int lastTick = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                int delay = samples[i].ticks - lastTick;
                while (delay > 0)
                {
                    // Never exceed the maximum delay value. Ramps will be truncated if necessary.
                    int safeDelay = Math.Min(delay, GloCommand.MAX_TICKS);
                    delay -= safeDelay;

                    if (samples[i].colBefore == lastColor)
                    {
                        container.Commands.Add(new GloDelayCommand(safeDelay));
                    }
                    else
                    {
                        container.Commands.Add(new GloRampCommand(samples[i].colBefore, safeDelay));
                        lastColor = samples[i].colBefore;
                    }
                }

                if (samples[i].colAfter != lastColor)
                {
                    container.Commands.Add(new GloColorCommand(samples[i].colAfter));
                    lastColor = samples[i].colAfter;
                }

                lastTick = samples[i].ticks;
            }

            return container;
        }

        private static void OptimizeCommands(List<GloCommand> commands)
        {
            // premise: the command list is still flat and does not contain any loops

            var sw = new Stopwatch();
            sw.Start();

            // optimize by detecting loops

            List<string> allLines = new List<string>(commands.Select(cmd => cmd.ToGloLines().Single())); // flat hierarchy ==> one line per command
            //List<string> lineBuffer = new List<string>(commands.Count / 2);
            //List<string> lineBuffer2 = new List<string>(commands.Count / 2);
            for (int loopPeriod = 2; loopPeriod <= commands.Count / 2; loopPeriod++)
            {
                for (int loopStart = 0; loopStart <= commands.Count - 2 * loopPeriod; loopStart++)
                {
                    //lineBuffer.Clear();
                    //lineBuffer.AddRange(Enumerable.Range(loopStart, loopPeriod).Select(i => allLines[i]));

                    int possibleRepetitions = (commands.Count - loopStart) / loopPeriod - 1;
                    int rep = 1;
                    while (rep <= possibleRepetitions)
                    {
                        //lineBuffer2.Clear();
                        //lineBuffer2.AddRange(Enumerable.Range(loopStart + loopPeriod * rep, loopPeriod).Select(i => allLines[i]));

                        int comparisonStart = loopStart + loopPeriod * rep;

                        if (RangeEqual(allLines, loopStart, comparisonStart, loopPeriod))
                            rep++;
                        else
                            break;
                    }

                    if (rep > 1)
                    {
                        Debug.WriteLine("found loop of period {0}, looping x{1}, starting at {2}", loopPeriod, rep, loopStart);
                        GloLoopCommand loop = new GloLoopCommand(rep);
                        loop.Commands.AddRange(Enumerable.Range(loopStart, loopPeriod).Select(i => commands[i]));

                        GloLoopCommand loop2 = null;

                        if (rep > 255)
                        {
                            int wrappedWhole = rep / 255;
                            int wrappedRemainder = rep % 255;

                            // after this: loop  --> multiples of 255
                            //             loop2 --> remainder [optional]

                            if (wrappedRemainder > 0)
                            {
                                loop2 = new GloLoopCommand(wrappedRemainder);
                                loop2.Commands.AddRange(loop.Commands);
                            }

                            loop.Repetitions = 255;
                            if (wrappedWhole > 1)
                            {
                                GloLoopCommand outerLoop = new GloLoopCommand(wrappedWhole);
                                outerLoop.Commands.Add(loop);
                                loop = outerLoop;
                            }
                        }

                        commands.RemoveRange(loopStart, rep * loopPeriod);
                        allLines.RemoveRange(loopStart, rep * loopPeriod);

                        commands.Insert(loopStart, loop);
                        allLines.Insert(loopStart, Guid.NewGuid().ToString()); // insert dummy line that won't match again to keep indices in sync
                        if (loop2 != null)
                        {
                            loopStart++;
                            commands.Insert(loopStart, loop2);
                            allLines.Insert(loopStart, Guid.NewGuid().ToString());
                        }
                    }
                }
            }

            sw.Stop();
            Debug.WriteLine("Loop optimization complete. Time: {0} ms", sw.ElapsedMilliseconds);

            // possible code optimization: extract Tuple<GloCommand, string> beforehand to minimize calls to ToGloLines()
        }

        private static void WriteCommands(GloCommandContainer container, string file)
        {
            // convert commands to their string representation
            var commandStrings = container.ToGloLines();

            var lines = Enumerable.Repeat("; Generated by Glow Sequencer version " + GetProgramVersion() + " at " + DateTime.Now + ".", 1).Concat(commandStrings);
            System.IO.File.WriteAllLines(file, lines, Encoding.ASCII);
        }

        private static bool RangeEqual<T>(IList<T> list, int startA, int startB, int count)
        {
            for (int i = 0; i < count; i++)
                if (!EqualityComparer<T>.Default.Equals(list[startA + i], list[startB + i]))
                    return false;

            return true;
        }

        //private static GloCommandContainer ExportCommands(Timeline timeline, Track track, IEnumerable<Block> blocks)
        //{
        //    GloCommandContainer allCommands = new GloCommandContainer("MAIN");
        //    int currentTicks = 0;
        //    float tickFractionsAcc = 0;

        //    float currentTime = 0;

        //    foreach (var block in blocks.SelectMany(b => FlattenBlock(b, track)))
        //    {
        //        float delayTime = block.StartTime - currentTime;
        //        currentTime = block.StartTime;

        //        if (delayTime < 0)
        //            throw new InvalidOperationException("blocks are overlapping at " + currentTime + " s");

        //        int gapTicks = DelayTicks(delayTime, ref currentTicks, ref tickFractionsAcc);
        //        if (gapTicks > 0)
        //            allCommands.Commands.Add(new GloDelayCommand(gapTicks));

        //        if (block is ColorBlock)
        //        {
        //            allCommands.Commands.Add(new GloColorCommand(((ColorBlock)block).Color));
        //            allCommands.Commands.Add(new GloDelayCommand(DelayTicks(block.Duration, ref currentTicks, ref tickFractionsAcc)));
        //            allCommands.Commands.Add(new GloColorCommand(GloColor.Black));

        //            currentTime = block.GetEndTime();
        //        }
        //        else if (block is RampBlock)
        //        {
        //            allCommands.Commands.Add(new GloColorCommand(((RampBlock)block).StartColor));
        //            allCommands.Commands.Add(new GloRampCommand(((RampBlock)block).EndColor, DelayTicks(block.Duration, ref currentTicks, ref tickFractionsAcc)));
        //            allCommands.Commands.Add(new GloColorCommand(GloColor.Black));

        //            currentTime = block.GetEndTime();
        //        }
        //        else
        //        {
        //            throw new NotImplementedException("unknown block type: " + block.GetType());
        //        }
        //    }

        //    return allCommands;
        //}

        //private static IEnumerable<Block> FlattenBlock(Block b, Track track)
        //{
        //    if (b is GroupBlock)
        //        return ((GroupBlock)b).Children.Where(child => child.Tracks.Contains(track)).SelectMany(child => FlattenBlock(child, track));
        //    else
        //        return Enumerable.Repeat(b, 1);
        //}


        //private static void PostprocessCommands(GloCommandContainer container)
        //{
        //    // - merge subsequent color commands
        //    // - merge subsequent delay commands
        //    for (int i = 0; i < container.Commands.Count - 1; i++)
        //    {
        //        GloCommand current = container.Commands[i];
        //        GloCommand next = container.Commands[i + 1];

        //        if (current is GloColorCommand && next is GloColorCommand)
        //        {
        //            // the first color is immediately overwritten, so it can be removed
        //            container.Commands.RemoveAt(i);
        //            i--;
        //        }
        //        else if (current is GloDelayCommand && next is GloDelayCommand)
        //        {
        //            // merge next onto current and remove next
        //            ((GloDelayCommand)current).DelayTicks += ((GloDelayCommand)next).DelayTicks;

        //            container.Commands.RemoveAt(i + 1);
        //            i--;

        //            // TO_DO insert comment stating the unmerged delays
        //        }
        //    }
        //}



        //public static void ExportGloFiles_Legacy(Timeline timeline, string filenameBase, string filenameSuffix)
        //{
        //    foreach (var track in timeline.Tracks)
        //    {
        //        GloCommandContainer allCommands = new GloCommandContainer(null, "END");
        //        int currentTicks = 0;
        //        float tickFractionsAcc = 0;

        //        float currentTime = 0;

        //        foreach (var block in track.Blocks.OrderBy(b => b.StartTime))
        //        {
        //            float delayTime = block.StartTime - currentTime;
        //            currentTime = block.StartTime;

        //            int gapTicks = DelayTicks(delayTime, ref currentTicks, ref tickFractionsAcc);
        //            if (gapTicks > 0)
        //                allCommands.Commands.Add(new GloDelayCommand(gapTicks));


        //            if (block is ColorBlock)
        //            {
        //                allCommands.Commands.Add(new GloColorCommand(((ColorBlock)block).Color));
        //                allCommands.Commands.Add(new GloDelayCommand(DelayTicks(block.Duration, ref currentTicks, ref tickFractionsAcc)));
        //                allCommands.Commands.Add(new GloColorCommand(GloColor.Black));

        //                currentTime = block.GetEndTime();
        //            }
        //            else if (block is RampBlock)
        //            {
        //                allCommands.Commands.Add(new GloColorCommand(((RampBlock)block).StartColor));
        //                allCommands.Commands.Add(new GloRampCommand(((RampBlock)block).EndColor, DelayTicks(block.Duration, ref currentTicks, ref tickFractionsAcc)));
        //                allCommands.Commands.Add(new GloColorCommand(GloColor.Black));

        //                currentTime = block.GetEndTime();
        //            }
        //        }


        //        // postprocess command structure:
        //        // - merge subsequent color commands
        //        // - merge subsequent delay commands
        //        for (int i = 0; i < allCommands.Commands.Count - 1; i++)
        //        {
        //            GloCommand current = allCommands.Commands[i];
        //            GloCommand next = allCommands.Commands[i + 1];

        //            if (current is GloColorCommand && next is GloColorCommand)
        //            {
        //                // the first color is immediately overwritten, so it can be removed
        //                allCommands.Commands.RemoveAt(i);
        //                i--;
        //            }
        //            else if (current is GloDelayCommand && next is GloDelayCommand)
        //            {
        //                // merge next onto current and remove next
        //                ((GloDelayCommand)current).DelayTicks += ((GloDelayCommand)next).DelayTicks;

        //                allCommands.Commands.RemoveAt(i + 1);
        //                i--;

        //                // TO_DO insert comment stating the unmerged delays
        //            }
        //        }

        //        /*allCommands.Commands.Add(new GloEndCommand()); make it compile */

        //        // convert commands to their string representation
        //        var commandStrings = allCommands.Commands.Select(cmd => string.Join(", ", Enumerable.Repeat(cmd.Name, 1).Concat(cmd.GetArguments().Select(arg => arg.ToString()))));

        //        var lines = Enumerable.Repeat("; Generated by Glow Sequencer version " + GetProgramVersion() + " at " + DateTime.Now + ".", 1).Concat(commandStrings);

        //        // write to file
        //        string sanitizedTrackName = System.IO.Path.GetInvalidFileNameChars().Aggregate(track.Label, (current, c) => current.Replace(c.ToString(), "")).Replace(' ', '_');
        //        string file = filenameBase + sanitizedTrackName + filenameSuffix;

        //        System.IO.File.WriteAllLines(file, lines, Encoding.ASCII);
        //    }
        //}

        //private static int DelayTicks(float delayTime, ref int currentTicks, ref float tickFractionsAcc)
        //{
        //    float exactTicks = delayTime * TICKS_PER_SECOND;
        //    int delayTicks = (int)exactTicks;

        //    tickFractionsAcc += exactTicks - delayTicks;

        //    int fractionBias = (int)tickFractionsAcc;
        //    if (fractionBias > 0)
        //    {
        //        // a whole tick was accumulated by now, so we add it to the current delay and adjust the accumlator
        //        tickFractionsAcc -= fractionBias;
        //        delayTicks += fractionBias;
        //    }

        //    currentTicks += delayTicks;

        //    return delayTicks;
        //}


        private static string GetProgramVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }
    }
}
