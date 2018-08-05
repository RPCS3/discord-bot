using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Threading.Tasks;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.Utils;

namespace CompatBot.EventHandlers.LogParsing
{
    internal static partial class LogParser
    {
        private static readonly byte[] Bom = {0xEF, 0xBB, 0xBF};

        public static async Task<LogParseState> ReadPipeAsync(PipeReader reader)
        {
            var currentSectionLines = new LinkedList<ReadOnlySequence<byte>>();
            var state = new LogParseState();
            bool skippedBom = false;
            long totalReadBytes = 0;
            ReadResult result;
            do
            {
                result = await reader.ReadAsync(Config.Cts.Token).ConfigureAwait(false);
                var buffer = result.Buffer;
                if (!skippedBom)
                {
                    if (buffer.Length < 3)
                        continue;

                    var potentialBom = buffer.Slice(0, 3);
                    if (potentialBom.ToArray().SequenceEqual(Bom))
                    {
                        reader.AdvanceTo(potentialBom.End);
                        totalReadBytes += potentialBom.Length;
                        skippedBom = true;
                        continue;
                    }
                    skippedBom = true;
                }
                SequencePosition? lineEnd;
                do
                {
                    if (currentSectionLines.Count > 0)
                        buffer = buffer.Slice(buffer.GetPosition(1, currentSectionLines.Last.Value.End));
                    lineEnd = buffer.PositionOf((byte)'\n');
                    if (lineEnd != null)
                    {
                        await OnNewLineAsync(buffer.Slice(0, lineEnd.Value), result.Buffer, currentSectionLines, state).ConfigureAwait(false);
                        if (state.Error != LogParseState.ErrorCode.None)
                        {
                            reader.Complete();
                            return state;
                        }

                        buffer = buffer.Slice(buffer.GetPosition(1, lineEnd.Value));
                    }
                } while (lineEnd != null);

                if (result.IsCanceled || Config.Cts.IsCancellationRequested)
                    state.Error = LogParseState.ErrorCode.SizeLimit;
                else if (result.IsCompleted)
                    await FlushAllLinesAsync(result.Buffer, currentSectionLines, state).ConfigureAwait(false);
                var sectionStart = currentSectionLines.Count == 0 ? buffer : currentSectionLines.First.Value;
                totalReadBytes += result.Buffer.Slice(0, sectionStart.Start).Length;
                reader.AdvanceTo(sectionStart.Start, buffer.End);
                if (totalReadBytes >= Config.LogSizeLimit)
                {
                    state.Error = LogParseState.ErrorCode.SizeLimit;
                    break;
                }
            } while (!(result.IsCompleted || result.IsCanceled || Config.Cts.IsCancellationRequested));
            reader.Complete();
            return state;
        }

        private static async Task OnNewLineAsync(ReadOnlySequence<byte> line, ReadOnlySequence<byte> buffer, LinkedList<ReadOnlySequence<byte>> sectionLines, LogParseState state)
        {
            var currentProcessor = SectionParsers[state.Id];
            if (line.AsString().Contains(currentProcessor.EndTrigger, StringComparison.InvariantCultureIgnoreCase))
            {
                await FlushAllLinesAsync(buffer, sectionLines, state).ConfigureAwait(false);
                SectionParsers[state.Id].OnSectionEnd?.Invoke(state);
                state.Id++;
                return;
            }
            if (sectionLines.Count == 50)
                await ProcessFirstLineInBufferAsync(buffer, sectionLines, state).ConfigureAwait(false);
            sectionLines.AddLast(line);
        }

        private static async Task FlushAllLinesAsync(ReadOnlySequence<byte> buffer, LinkedList<ReadOnlySequence<byte>> sectionLines, LogParseState state)
        {
            while (sectionLines.Count > 0 && state.Error == LogParseState.ErrorCode.None)
                await ProcessFirstLineInBufferAsync(buffer, sectionLines, state).ConfigureAwait(false);
        }

        private static async Task ProcessFirstLineInBufferAsync(ReadOnlySequence<byte> buffer, LinkedList<ReadOnlySequence<byte>> sectionLines, LogParseState state)
        {
            var currentProcessor = SectionParsers[state.Id];
            var firstSectionLine = sectionLines.First.Value.AsString();
            await currentProcessor.OnLineCheckAsync(firstSectionLine, state).ConfigureAwait(false);
            if (state.Error != LogParseState.ErrorCode.None)
                return;

            var section = buffer.Slice(sectionLines.First.Value.Start, sectionLines.Last.Value.End).AsString();
            currentProcessor.OnExtract?.Invoke(firstSectionLine, section, state);
            sectionLines.RemoveFirst();

        }
    }
}
