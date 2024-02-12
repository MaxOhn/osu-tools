// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using JetBrains.Annotations;
using McMaster.Extensions.CommandLineUtils;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace PerformanceCalculator.Simulate
{
    public abstract class SimulateCommand : ProcessorCommand
    {
        public abstract Ruleset Ruleset { get; }

        [UsedImplicitly]
        [Required]
        [Argument(0, Name = "beatmap", Description = "Required. Can be either a path to beatmap file (.osu) or beatmap ID.")]
        public string Beatmap { get; }

        [UsedImplicitly]
        public virtual double Accuracy { get; }

        [UsedImplicitly]
        public virtual int? Combo { get; }

        [UsedImplicitly]
        public virtual double PercentCombo { get; }

        [UsedImplicitly]
        public virtual string[] Mods { get; }

        [UsedImplicitly]
        public virtual int Misses { get; }

        [UsedImplicitly]
        public virtual int? Mehs { get; }

        [UsedImplicitly]
        public virtual int? Goods { get; }

        [UsedImplicitly]
        [Option(Template = "-nc|--no-classic", Description = "Excludes the classic mod.")]
        public bool NoClassicMod { get; }

        public virtual int? GetScore(Mod[] mods) => null;

        public override void Execute()
        {
            var ruleset = Ruleset;

            var workingBeatmap = ProcessorWorkingBeatmap.FromFileOrId(Beatmap);
            var allMods = NoClassicMod ? GetMods(ruleset) : GetMods(ruleset).Select(mods => LegacyHelper.FilterDifficultyAdjustmentMods(workingBeatmap.BeatmapInfo, ruleset, mods));
            // var allMods = NoClassicMod ? GetMods(ruleset) : GetMods(ruleset).Select(mods => LegacyHelper.ConvertToLegacyDifficultyAdjustmentMods(workingBeatmap.BeatmapInfo, ruleset, mods));

            var results = new List<Result>();
            var difficultyCalculator = ruleset.CreateDifficultyCalculator(workingBeatmap);
            var performanceCalculator = ruleset.CreatePerformanceCalculator();

            foreach (Mod[] mods in allMods)
            {
                var beatmap = workingBeatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

                var beatmapMaxCombo = GetMaxCombo(beatmap);
                var maxCombo = Combo ?? (int)Math.Round(PercentCombo / 100 * beatmapMaxCombo);
                var statistics = GenerateHitResults(Accuracy / 100, beatmap, Misses, Mehs, Goods);
                var score = GetScore(mods) ?? 0;
                var accuracy = GetAccuracy(statistics);

                var difficultyAttributes = difficultyCalculator.Calculate(mods);

                var ppAttributes = performanceCalculator?.Calculate(new ScoreInfo(beatmap.BeatmapInfo, ruleset.RulesetInfo)
                {
                    Accuracy = accuracy,
                    MaxCombo = maxCombo,
                    Statistics = statistics,
                    Mods = mods,
                    TotalScore = score,
                }, difficultyAttributes);

                var result = new Result
                {
                    Score = new ScoreStatistics
                    {
                        RulesetId = ruleset.RulesetInfo.OnlineID,
                        BeatmapId = workingBeatmap.BeatmapInfo.OnlineID,
                        Beatmap = workingBeatmap.BeatmapInfo.ToString(),
                        Mods = mods.Select(m => new APIMod(m)).ToList(),
                        TotalScore = score,
                        Accuracy = accuracy * 100,
                        Combo = maxCombo,
                        Statistics = statistics
                    },
                    PerformanceAttributes = ppAttributes,
                    DifficultyAttributes = difficultyAttributes
                };

                results.Add(result);
            }

            OutputPerformances(results);
        }

        protected Mod[][] GetMods(Ruleset ruleset)
        {
            if (Mods == null)
                return new Mod[][] { Array.Empty<Mod>() };

            var availableMods = ruleset.CreateAllMods().ToList();
            var mods = new List<Mod[]>();

            foreach (var modString in Mods)
            {
                int endIdx = 2;
                var newMods = new List<Mod>();

                if (modString.ToLower() == "nm")
                    endIdx = modString.Length + 1;

                while (endIdx <= modString.Length)
                {
                    var modSubstring = modString.Substring(endIdx - 2, 2);
                    Mod newMod = availableMods.FirstOrDefault(m => string.Equals(m.Acronym, modSubstring, StringComparison.CurrentCultureIgnoreCase));
                    if (newMod == null)
                        throw new ArgumentException($"Invalid mod provided: {modSubstring}");

                    newMods.Add(newMod);
                    endIdx += 2;
                }

                mods.Add(newMods.ToArray());
            }

            return mods.ToArray();
        }

        protected abstract int GetMaxCombo(IBeatmap beatmap);

        protected abstract Dictionary<HitResult, int> GenerateHitResults(double accuracy, IBeatmap beatmap, int countMiss, int? countMeh, int? countGood);

        protected virtual double GetAccuracy(Dictionary<HitResult, int> statistics) => 0;
    }
}
