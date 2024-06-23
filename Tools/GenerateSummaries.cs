using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace ArknightsWorkshop.Tools;

public class GenerateSummaries(Config config) : Tool
{
    public override string Name => "Generate summaries about items, characters, stages";

    private FrozenDictionary<string, FrozenSet<string>> files = null!;
    private string versionPath = null!;

    private Dictionary<string, ItemDesc> items = [];
    private Dictionary<string, StageDesc> stages = [];
    private Dictionary<string, CharacterDesc> chars = [];
    private Dictionary<string, SkillDesc> skills = [];
    private Dictionary<string, Dictionary<string, SkinDesc>> skins = [];
    private Dictionary<string, List<string>> charModules = [];
    private Dictionary<string, string> subClassMap = [];
    private Dictionary<string, string> modMissions = [];
    private Dictionary<string, ModuleDesc> modules = [];

    public override ValueTask Run(CancellationToken cancel)
    {
        var ver = ConsoleUI.SelectVersion(config.WorkingDirectory);
        if (ver is null) return ValueTask.CompletedTask;
        versionPath = Path.Combine(config.WorkingDirectory, Paths.Assets, ver);
        files = Util.GetFileTree(Path.Combine(versionPath, Paths.Processed))
            .GroupBy(k => Path.GetFileName(k)).ToFrozenDictionary(k => k.Key, v => v.ToFrozenSet());

        if (!GenerateItemSummary()) return ValueTask.CompletedTask;
        if (!GenerateStageSummary()) return ValueTask.CompletedTask;
        if (!GenerateCharacterSummary()) return ValueTask.CompletedTask;
        if (!GenerateSkillSummary()) return ValueTask.CompletedTask;
        if (!GenerateSkinSummary()) return ValueTask.CompletedTask;
        if (!GenerateModuleSummary()) return ValueTask.CompletedTask;
#if DEBUG
        var summPath = Path.Combine(versionPath, Paths.Summary);
        if (Directory.Exists(summPath)) Directory.Delete(summPath, true);
#endif

        Console.WriteLine("Writing...");
        var threads = ((IEnumerable<ThreadStart>)
            [WriteItemSummary, WriteStageSummary, WriteCharacterSummary])
            .Select(t => new Thread(t))
            .ToArray();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        return ValueTask.CompletedTask;
    }

    private bool GenerateItemSummary()
    {
        Console.WriteLine("Generating item summary...");
        using var json = GetJson("item_table");
        if (json is null) return false;

        foreach (var pair in json.RootElement.GetProperty("items").EnumerateArray())
        {
            var item = pair.GetProperty("value");
            var id = item.GetProperty("itemId").GetString()!;
            var name = Util.Unescape(item.GetProperty("name").GetString()!);
            var description = Util.Unescape(item.GetStringPropOr("description"));
            var usage = Util.Unescape(item.GetStringPropOr("obtainApproach"));
            var obtain = Util.Unescape(item.GetStringPropOr("obtainApproach"));
            int? tier = item.TryGetProperty("rarity", out var p2) ? int.Parse(p2.GetString().AsSpan(5)) : default;
            var iconPaths = files.TryGetValue($"{item.GetProperty("iconId").GetString()!}.png", out var l) ? l : FrozenSet<string>.Empty;

            var stages = new List<(string, string?)>();
            foreach (var stage in item.GetProperty("stageDropList").EnumerateArray())
                stages.Add((
                    stage.GetProperty("stageId").GetString()!,
                    stage.GetStringPropOr("occPer")
                ));

            var crafts = new List<(string, long)>();
            foreach (var craft in item.GetProperty("buildingProductList").EnumerateArray())
                crafts.Add((
                    craft.GetProperty("roomType").GetString()!,
                    long.Parse(craft.GetProperty("formulaId").GetString()!)
                ));

            items[id] = new(name, description, usage, obtain, tier, iconPaths, stages, crafts);
        }
        return true;
    }

    private bool GenerateStageSummary()
    {
        Console.WriteLine("Generating stages summary...");
        using var json = GetJson("stage_table");
        if (json is null) return false;

        foreach (var pair in json.RootElement.GetProperty("stages").EnumerateArray())
        {
            var key = pair.GetProperty("key").GetString()!;
            var stage = pair.GetProperty("value");

            var code = stage.GetProperty("code").GetString()!;
            var name = Util.Unescape(stage.GetStringPropOr("name", ""));
            var desc = Util.Unescape(stage.GetStringPropOr("description", ""));
            var zone = stage.GetProperty("zoneId").GetString()!;
            var unitLv = stage.GetStringPropOr("dangerLevel", "-");
            var isCM = stage.GetProperty("difficulty").GetString() == "FOUR_STAR";
            var env = stage.GetStringPropOr("diffGroup");
            var san = stage.TryGetProperty("apCost", out var sanProp) ? sanProp.GetInt32() : 0;

            var drops = new List<StageDropDesc>();
            if (stage.TryGetProperty("stageDropInfo", out var dropProp))
                foreach (var drop in dropProp.GetProperty("displayDetailRewards").EnumerateArray())
                    drops.Add(new(
                        drop.GetProperty("id").GetString()!,
                        drop.GetStringPropOr("occPercent"),
                        drop.GetStringPropOr("dropType")
                    ));

            stages[key] = new(code, name, desc, zone, unitLv!, isCM, env, san, drops);
        }
        return true;
    }

    private bool GenerateCharacterSummary()
    {
        Console.WriteLine("Generating characters summary...");
        using var json = GetJson("character_table");
        if (json is null) return false;

        foreach (var pair in json.RootElement.GetProperty("characters").EnumerateArray())
        {
            var key = pair.GetProperty("key").GetString()!;
            var value = pair.GetProperty("value");

            var name = value.GetProperty("name").GetString()!;
            var desc = value.GetStringPropOr("description");
            var tokId = value.GetStringPropOr("potentialItemId");
            var kernTokId = value.GetStringPropOr("classicPotentialItemId");
            var pos = value.GetStringPropOr("position", "MELEE")!;
            var tags = new List<string>();
            if (value.TryGetProperty("tagList", out var taglPr))
                foreach (var tag in taglPr.EnumerateArray())
                    tags.Add(tag.GetString()!);
            var itUsag = value.GetStringPropOr("itemUsage");
            var itDesc = value.GetStringPropOr("itemDesc");
            var rarity = value.TryGetProperty("rarity", out var rarPr) ? rarPr.GetString()![5] - '0' : 1;
            var uclass = value.GetProperty("profession").GetString()!;
            var uarchc = value.GetProperty("subProfessionId").GetString()!;

            var elites = new List<CharEliteDesc>();
            foreach (var elite in value.GetProperty("phases").EnumerateArray())
            {
                var range = elite.GetStringPropOr("rangeId", "0-1")!;
                var attrs = new List<CharStats>();
                foreach (var attr in elite.GetProperty("attributesKeyFrames").EnumerateArray())
                {
                    var data = attr.GetProperty("data");
                    var lv = attr.GetProperty("level").GetInt32();
                    var hp = data.GetProperty("maxHp").GetInt32();
                    var atk = data.TryGetProperty("atk", out var atkProp) ? atkProp.GetInt32() : 0;
                    var def = data.TryGetProperty("def", out var defPr) ? defPr.GetInt32() : 0;
                    var res = data.TryGetProperty("magicResistance", out var resPr) ? resPr.GetSingle() : 0f;
                    var dp = data.TryGetProperty("cost", out var cPr) ? cPr.GetInt32() : 0;
                    var block = data.TryGetProperty("blockCnt", out var blkPr) ? blkPr.GetInt32() : 0;
                    var atkint = data.GetProperty("baseAttackTime").GetSingle();
                    var redept = data.TryGetProperty("respawnTime", out var rspPr) ? rspPr.GetInt32() : 0;
                    attrs.Add(new(lv, hp, atk, def, res, dp, block, atkint, redept));
                }
                var cost = new List<(string, int)>();
                if (elite.TryGetProperty("evolveCost", out var costProp))
                    foreach (var item in costProp.EnumerateArray())
                        cost.Add((
                            item.GetProperty("id").GetString()!,
                            item.GetProperty("count").GetInt32()
                        ));
                elites.Add(new(range, attrs, cost));
            }

            var skills = new List<CharSkillDesc>();
            foreach (var skill in value.GetProperty("skills").EnumerateArray())
            {
                if (!skill.TryGetProperty("skillId", out var idProp)) continue;
                var id = idProp.ToString();

                var mast = new List<List<(string, int)>>();
                foreach (var mProp in skill.GetProperty("levelUpCostCond").EnumerateArray())
                {
                    var cost = new List<(string, int)>();
                    if (mProp.TryGetProperty("levelUpCost", out var lvUpCost))
                        foreach (var itemC in lvUpCost.EnumerateArray())
                            cost.Add((
                                itemC.GetProperty("id").GetString()!,
                                itemC.GetProperty("count").GetInt32()
                            ));
                    mast.Add(cost);
                }

                skills.Add(new(id, mast));
            }

            var talents = new List<CharTalentDesc>();
            if (value.TryGetProperty("talents", out var talProp))
                foreach (var talent in talProp.EnumerateArray())
                    if (talent.TryGetProperty("candidates", out var talcProp))
                        foreach (var talentc in talcProp.EnumerateArray())
                        {
                            var prKey = talentc.GetProperty("prefabKey").GetString()!;
                            var ind = prKey[0] - '0';
                            var byModule = prKey.Length > 1;
                            var unCond = talentc.GetProperty("unlockCondition");
                            var needLv = unCond.GetProperty("level").GetInt32();
                            var needEl = unCond.TryGetProperty("phase", out var phProp) ? phProp.GetString()![6] - '0' : 0;
                            var needPt = talentc.TryGetProperty("requiredPotentialRank", out var tlProp) ? tlProp.GetInt32() : 0;
                            var tname = talentc.GetStringPropOr("name", "")!;
                            var tdesc = talentc.GetStringPropOr("description", "")!;

                            talents.Add(new(ind, needEl, needLv, needPt, byModule, tname, tdesc));
                        }

            var potentials = new List<string>();
            foreach (var pot in value.GetProperty("potentialRanks").EnumerateArray())
                potentials.Add(pot.GetProperty("description").GetString()!);

            var trusts = new List<TrustBonusDesc>();
            if (value.TryGetProperty("favorKeyFrames", out var trustProp))
                foreach (var tr in trustProp.EnumerateArray())
                {
                    var lv = tr.TryGetProperty("level", out var lvProp) ? lvProp.GetInt32() : 0;
                    var atk = 0;
                    var def = 0;
                    var hp = 0;
                    foreach (var p in tr.GetProperty("data").EnumerateObject())
                    {
                        if (p.NameEquals("atk")) atk = p.Value.GetInt32();
                        else if (p.NameEquals("maxHp")) hp = p.Value.GetInt32();
                        else if (p.NameEquals("def")) def = p.Value.GetInt32();
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"'{name}': unknown trust bonus attribute '{p.Name}'");
                            Console.ResetColor();
                        }
                    }
                    trusts.Add(new(lv, atk, def, hp));
                }

            var skillsUp = new List<List<(string, int)>>();
            foreach (var mProp in value.GetProperty("allSkillLvlup").EnumerateArray())
            {
                var skillUp = new List<(string, int)>();
                if (mProp.TryGetProperty("lvlUpCost", out var cUpCost))
                    foreach (var itemC in cUpCost.EnumerateArray())
                        skillUp.Add((
                            itemC.GetProperty("id").GetString()!,
                            itemC.GetProperty("count").GetInt32()
                        ));
                skillsUp.Add(skillUp);
            }

            chars[key] = new(name, desc, tokId, kernTokId, pos, tags, itUsag, itDesc, rarity,
                uclass, uarchc, elites, skills, talents, potentials, trusts, skillsUp);
        }
        return true;

    }

    private bool GenerateSkillSummary()
    {
        Console.WriteLine("Generating skills summary...");
        using var json = GetJson("skill_table");
        if (json is null) return false;

        foreach (var pair in json.RootElement.GetProperty("skills").EnumerateArray())
        {
            var key = pair.GetProperty("key").GetString()!;
            var skill = pair.GetProperty("value");
            var icon = skill.GetStringPropOr("iconId");

            // I don't think any skill will change it's name or charging/activation type when leveled up
            var sumData = skill.GetProperty("levels")[0];
            var name = Util.Unescape(sumData.GetProperty("name").GetString());
            var range = sumData.GetStringPropOr("rangeId");
            var actT = sumData.GetStringPropOr("skillType", "AUTO")!;
            var chgProp = sumData.GetProperty("spData").GetProperty("spType");
            var chgT = chgProp.ValueKind switch
            {
                JsonValueKind.String => chgProp.GetString()!,
                JsonValueKind.Number => chgProp.GetInt32() switch
                {
                    8 => "PASSIVE",
                    _ => "UNKNOWN"
                },
                _ => throw new("Unknown activation type JsonType")
            };

            var levels = new List<SkillLevelDesc>();
            foreach (var level in skill.GetProperty("levels").EnumerateArray())
            {
                var desc = level.GetStringPropOr("description", "")!;
                var spData = level.GetProperty("spData");
                var sp = spData.TryGetProperty("spCost", out var spPr) ? spPr.GetInt32() : 0;
                var init = spData.TryGetProperty("initSp", out var initPr) ? initPr.GetInt32() : 0;
                var dur = level.TryGetProperty("duration", out var durP) ? durP.GetSingle() : SkillLevelDesc.InfiniteDuration;
                var dict = new Dictionary<string, string>();
                foreach (var bEntry in level.GetProperty("blackboard").EnumerateArray())
                {
                    if (!bEntry.TryGetProperty("value", out var val)) continue;
                    dict[bEntry.GetProperty("key").GetString()!.ToLower()] = val.ValueKind switch
                    {
                        JsonValueKind.String => val.GetString()!,
                        JsonValueKind.Number => val.GetDouble().ToString(),
                        _ => throw new("Unexpected JsonType in skill blackboard")
                    };
                }
                levels.Add(new(desc, sp, init, dur, dict));
            }
            skills[key] = new(name, icon, actT, chgT, range, levels);
        }
        return true;
    }

    private bool GenerateSkinSummary()
    {
        Console.WriteLine("Generating skin summary...");
        using var json = GetJson("skin_table");
        if (json is null) return false;

        foreach (var pair in json.RootElement.GetProperty("charSkins").EnumerateArray())
        {
            var key = pair.GetProperty("key").GetString()!;
            var skin = pair.GetProperty("value");
            var disp = skin.GetProperty("displaySkin");

            var charId = skin.GetProperty("charId").GetString()!;
            var avatId = skin.GetProperty("avatarId").GetString()!;
            var portId = skin.GetStringPropOr("portraitId");
            var name = Util.Unescape(disp.GetStringPropOr("skinName"));
            var auths = new List<string>();
            if (disp.TryGetProperty("drawerList", out var dispProp))
                foreach (var auth in dispProp.EnumerateArray())
                    auths.Add(Util.Unescape(auth.GetString()!));
            var grop = Util.Unescape(disp.GetStringPropOr("skinGroupName"));
            var cont = Util.Unescape(disp.GetStringPropOr("content"));
            var diag = Util.Unescape(disp.GetStringPropOr("dialog"));
            var usag = Util.Unescape(disp.GetStringPropOr("usage"));
            var desc = Util.Unescape(disp.GetStringPropOr("description"));

            if (!skins.TryGetValue(charId, out var charSkins))
                charSkins = skins[charId] = [];
            charSkins[key] = new(avatId, portId, name, auths, grop, cont, diag, usag, desc);
        }
        return true;
    }

    private bool GenerateModuleSummary()
    {
        Console.WriteLine("Generating module summary...");
        using var json = GetJson("uniequip_table");
        if (json is null) return false;

        foreach (var pair in json.RootElement.GetProperty("charEquip").EnumerateArray())
        {
            var key = pair.GetProperty("key").GetString()!;
            var mods = new List<string>();
            foreach (var charMod in pair.GetProperty("value").EnumerateArray())
                mods.Add(charMod.GetString()!);
            charModules[key] = mods;
        }
        foreach (var pair in json.RootElement.GetProperty("subProfDict").EnumerateArray())
        {
            var key = pair.GetProperty("key").GetString()!;
            var val = pair.GetProperty("value").GetProperty("subProfessionName").GetString()!;
            subClassMap[key] = val;
        }
        foreach (var pair in json.RootElement.GetProperty("missionList").EnumerateArray())
        {
            var key = pair.GetProperty("key").GetString()!;
            var val = Util.Unescape(pair.GetProperty("value").GetProperty("desc").GetString())!;
            modMissions[key] = val;
        }
        foreach (var pair in json.RootElement.GetProperty("equipDict").EnumerateArray())
        {
            var key = pair.GetProperty("key").GetString()!;
            var mod = pair.GetProperty("value");

            var name = Util.Unescape(mod.GetProperty("uniEquipName").GetString())!;
            var icon = mod.GetProperty("uniEquipIcon").GetString()!;
            var desc = Util.Unescape(mod.GetProperty("uniEquipDesc").GetString())!;
            var type = mod.GetProperty("typeIcon").GetString()!.ToUpper();

            var missions = new List<string>();
            foreach(var missPr in mod.GetProperty("missionList").EnumerateArray())
                missions.Add(missPr.GetString()!);

            var costs = new List<List<(string, int)>>();
            if(mod.TryGetProperty("itemCost", out var costsProp))
                foreach (var missPr in costsProp.EnumerateArray())
                {
                    var level = missPr.GetProperty("key").GetInt32() - 1;
                    while (costs.Count <= level) costs.Add(null!);
                    var cost = new List<(string, int)>();

                    foreach (var item in missPr.GetProperty("value").EnumerateArray())
                        cost.Add((
                            item.GetProperty("id").GetString()!,
                            item.GetProperty("count").GetInt32()
                        ));
                    costs[level] = cost;
                }

            modules[key] = new(name, icon, desc, type, missions, costs);
        }

        return true;
    }
    
    private void WriteItemSummary()
    {
        var dir = Path.Combine(versionPath, Paths.ItemSummary);
        var done = 0;
        var names = new string[items.Count];
        var keys = new string[items.Count];
        foreach (var (key, item) in items)
        {
            var folder = Path.Combine(dir, key);
            Directory.CreateDirectory(folder);
            for (int i = 0; i < item.IconPath.Count; i++)
            {
                var path = item.IconPath.Items[i];
                File.Copy(path, Path.Combine(folder, $"icon.{i}{Path.GetExtension(path)}"));
            }

            names[done] = item.Name;
            keys[done] = key;
            done++;

            using var write = new StreamWriter(Path.Combine(folder, "description.txt"));
            write.WriteLine($"Name:        {item.Name}");
            if (item.Tier.HasValue)
                write.WriteLine($"Tier:        {item.Tier.Value}");
            if (item.Description is not null)
            {
                write.WriteLine("Description:");
                write.WriteLine(item.Description);
            }
            if (item.Usage is not null)
                write.WriteLine($"Usage:       {item.Usage}");
            if (item.Obtain is not null)
                write.WriteLine($"How to get:  {item.Obtain}");
            if (item.Stages.Count > 0)
            {
                write.WriteLine($"Stages:");
                foreach (var p in item.Stages)
                    write.WriteLine(new string(' ', 13) +
                        p.Chance switch
                        {
                            string c => $"{stages[p.Stage].DisplayCode}, {c}",
                            _ => stages[p.Stage].DisplayCode
                        });
            }
        }
        Array.Sort(names, keys);
        using var lookup = new StreamWriter(Path.Combine(dir, "lookup.txt"));
        for (int i = 0; i < done; i++)
            lookup.WriteLine($"{names[i]}  ->  {keys[i]}");
    }

    private void WriteStageSummary()
    {
        var dir = Path.Combine(versionPath, Paths.StageSummary);
        var done = 0;
        var names = new string[stages.Count];
        var keys = new string[stages.Count];
        foreach (var (key, stage) in stages)
        {
            var folder = Path.Combine(dir, stage.ZoneID);
            Directory.CreateDirectory(folder);

            using var write = new StreamWriter(Path.Combine(folder, $"{key}.txt"));
            write.WriteLine($"Code:            {stage.Code}");
            write.WriteLine($"Name:            {stage.Name}");
            write.WriteLine($"Description:\r\n\r\n{stage.Description}\r\n");
            write.WriteLine($"Recommended lv.: {stage.UnitLevel}");
            write.WriteLine($"Sanity cost:     {stage.SanityCost}");

            if (stage.Drops.Count > 0)
            {
                write.WriteLine($"Drops:");
                foreach (var drop in stage.Drops)
                {
                    if (items.TryGetValue(drop.Id, out var item))
                        write.Write(new string(' ', 17) + $"Item: {item.Name}");
                    else if (chars.TryGetValue(drop.Id, out var charc))
                        write.Write(new string(' ', 17) + $"Character: {charc.Name}");
                    else
                        write.Write($"???: {drop.Id}");
                    if (drop.Chance is not null) write.Write($", Chance:{drop.Chance}");
                    if (drop.Type is not null) write.Write($", How:{drop.Type}");
                    write.WriteLine();
                }
            }

            names[done] = $"{stage.DisplayCode} \"{stage.Name}\"";
            keys[done] = Path.Combine(stage.ZoneID, key);
            done++;
        }
        Array.Sort(names, keys);
        using var lookup = new StreamWriter(Path.Combine(dir, "lookup.txt"));
        for (int i = 0; i < done; i++)
            lookup.WriteLine($"{names[i]}  ->  {keys[i]}");
    }

    private void WriteCharacterSummary()
    {
        var dir = Path.Combine(versionPath, Paths.CharactersSummary);
        var done = 0;
        var names = new string[chars.Count];
        var keys = new string[chars.Count];
        foreach (var (key, charc) in chars)
        {
            var isOper = !key.StartsWith("trap") && !key.StartsWith("token");

            var folder = Path.Combine(dir, key);
            Directory.CreateDirectory(folder);

            using var write = new StreamWriter(Path.Combine(folder, $"description.txt"));
            write.WriteLine($"\"{charc.Name}\" - {charc.Rarity}★ {subClassMap[charc.SubClass]} {charc.Class}");
            write.WriteLine($"  | {charc.Description}");
            write.WriteLine($"  | {charc.ItemUsage}");
            write.WriteLine($"  | {charc.ItemDesc}");
            write.WriteLine($"Tags: {string.Join(", ", charc.Tags)}");
            write.Write($"Tokens: ");
            if (charc.TokenId is string tid && items.TryGetValue(tid, out var tok))
                write.Write($"{tok.Name}");
            if (charc.KernelTokenId is string ktid && items.TryGetValue(ktid, out var ktok))
                write.Write($" | {ktok.Name}");
            write.WriteLine();

            write.WriteLine();
            write.WriteLine("Stats:");
            write.WriteLine("Level   HP      ATK     DEF     RES     DP      Block   Atk.Int.  Redeploy");
            for (int i = 0; i < charc.Elites.Count; i++)
                foreach (var stats in charc.Elites[i].Stats)
                    write.WriteLine($"{$"E{i}L{stats.Level}",-8}{stats.MaxHP,-8}{stats.ATK,-8}{stats.DEF,-8}{stats.RES,-8}{stats.DP,-8}{stats.Block,-8}{stats.AttackTime,-10}{stats.RespawnTime,-8}");
            for (int i = 1; i < charc.Elites.Count; i++)
                write.WriteLine($"E{i} cost: {string.Join(", ", charc.Elites[i].Materials.Select(p => $"{p.Item2}× {items[p.Item1].Name}"))}");
            // TODO: range

            if (charc.Skills.Count > 0)
            {
                write.WriteLine();
                write.WriteLine("Common skill upgrade cost:");
                for (int i = 0; i < charc.SkillLevelCosts.Count; i++)
                    write.WriteLine($"  L{i + 2}: {string.Join(", ", charc.SkillLevelCosts[i].Select(p => $"{p.Item2}× {items[p.Item1].Name}"))}");
                for (int i = 0; i < charc.Skills.Count; i++)
                {
                    // TODO: skill range
                    using var swrite = new StreamWriter(Path.Combine(folder, $"skill{i + 1}.txt"));
                    var skill = skills[charc.Skills[i].Id];

                    if (files.TryGetValue($"skill_icon_{skill.IconId ?? charc.Skills[i].Id}.png", out var paths))
                    {
                        var path = paths.Single();
                        File.Copy(path, Path.Combine(folder, $"skill{i + 1}_icon{Path.GetExtension(path)}"));
                    }
                    swrite.Write($"  Skill {i + 1} \"{skill.Name}\" [{skill.ActivationType}");
                    if (skill.Levels.Any(l => l.SP != 0))
                        swrite.Write($", {skill.ChargeType} charge");
                    swrite.WriteLine("]:");
                    for (int j = 0; j < skill.Levels.Count; j++)
                    {
                        var lvl = skill.Levels[j];
                        swrite.WriteLine();
                        swrite.Write($"    {((j < 7) ? $"L{j + 1}" : $"M{j - 6}")} [");
                        if (lvl.SP > 0)
                            swrite.Write($"{lvl.SP} ({lvl.InitSP} init) SP, ");
                        swrite.WriteLine($"{lvl.Duration switch
                        {
                            SkillLevelDesc.InfiniteDuration => "infinite duration",
                            var x => $"{x} seconds"
                        }}]:");
                        swrite.WriteLine(lvl.FormatDescription());
                        swrite.WriteLine($"      Advanced: [{string.Join("; ", lvl.Blackboard.Select(p => $"{p.Key}: {p.Value}"))}]");
                        if (j >= 7)
                            swrite.WriteLine($"      Cost: {string.Join(", ", charc.Skills[i].MasteryCost[j - 7].Select(p => $"{p.Item2}× {items[p.Item1].Name}"))}");
                    }
                }
            }

            write.WriteLine();
            write.WriteLine("Trust bonus:");
            foreach (var trs in charc.TrustBonuses)
                write.WriteLine($"    {trs.Level}% - {trs.ATK} ATK; {trs.DEF} DEF; {trs.MaxHP} Max HP");

            write.WriteLine();
            write.WriteLine("Talents:");
            foreach (var tal in charc.Talents)
            {
                write.Write($"    {tal.Index}: {tal.Name} [E{tal.EliteNeed}L{tal.LevelNeed} Pot{tal.PotentialNeed}");
                if (tal.ByModule) write.Write("+Module");
                write.WriteLine($"]  -  {tal.Description}");
            }

            write.WriteLine();
            write.WriteLine("Potentials:");
            foreach (var pot in charc.Potentials)
                write.WriteLine($"    {pot}");

            if (skins.TryGetValue(key, out var charSkins))
            {
                write.WriteLine();
                write.WriteLine("Skins:");
                foreach (var (skinKey, skin) in charSkins)
                {
                    string[] avatPaths;
                    if (files.TryGetValue($"{skin.AvatarId}.png", out var avatFiles) &&
                        (avatPaths = avatFiles.Where(p => p.Contains("avatar")).ToArray()).Length == 1)
                        File.Copy(avatPaths[0], Path.Combine(folder, $"AVATAR__{skinKey}.png"));
                    else if (isOper)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Couldn't find skin avatar: '{skinKey}' of character '{key}'");
                        Console.ResetColor();
                    }
                    if (skin.PortraitId is not null)
                    {
                        string[] skinPaths;
                        if (files.TryGetValue($"{skin.PortraitId}.png", out var fls) &&
                            (skinPaths = fls.Where(p => p.Contains("skin") || p.Contains("chararts")).ToArray()).Length == 1)
                        {
                            File.Copy(skinPaths[0], Path.Combine(folder, $"PORTRAIT__{skinKey}.png"));
                        }
                        else if (isOper)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Couldn't find skin portrait: '{skinKey}' of character '{key}'");
                            Console.ResetColor();
                        }
                    }

                    write.WriteLine();
                    write.WriteLine($"  [{skinKey}] \"{skin.Name ?? "unnamed"}\" | \"{skin.Group ?? "no group"}\"");
                    write.WriteLine($"  Authors: {string.Join("; ", skin.Authors)}");
                    if (skin.Content is not null)
                    {
                        write.WriteLine($"  Content:");
                        write.WriteLine(skin.Content);
                    }
                    if (skin.Dialog is not null)
                    {
                        write.WriteLine($"  Dialog:");
                        write.WriteLine(skin.Dialog);
                    }
                    if (skin.Usage is not null)
                    {
                        write.WriteLine($"  Usage:");
                        write.WriteLine(skin.Usage);
                    }
                    if (skin.Description is not null)
                    {
                        write.WriteLine($"  Description:");
                        write.WriteLine(skin.Description);
                    }
                }
            }

            if (charModules.TryGetValue(key, out var mods))
                foreach(var modKey in mods)
                {
                    var mod = modules[modKey];
                    if(mod.Icon != "original")
                    {
                        if (files.TryGetValue($"{mod.Icon}.png", out var paths))
                            File.Copy(paths.Single(f => f.Contains("equip")), Path.Combine(folder, $"ICON_{mod.Type}.png"));
                        else if (isOper)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Couldn't find module icon: '{modKey}' of character '{key}'");
                            Console.ResetColor();
                        }
                    }

                    using var mwrite = new StreamWriter(Path.Combine(folder, $"{mod.Type}.txt"));
                    mwrite.WriteLine($"  \"{mod.Name}\"");
                    mwrite.WriteLine("  Description:");
                    mwrite.WriteLine(mod.Description);
                    if (mod.Missions.Count != 0)
                    {
                        mwrite.WriteLine();
                        mwrite.WriteLine("  Missions:");
                        for (int i = 0; i < mod.Missions.Count; i++)
                            mwrite.WriteLine($" {i + 1}) {modMissions[mod.Missions[i]]}");
                    }
                    if(mod.Cost.Count != 0)
                    {
                        mwrite.WriteLine();
                        mwrite.WriteLine("  Cost:");
                        for (int i = 0; i < mod.Cost.Count; i++)
                            mwrite.WriteLine($"    L{i + 1}) {string.Join(", ", mod.Cost[i].Select(p => $"{p.Item2}× {items[p.Item1].Name}"))}");
                    }
                }
            // TODO: voices, files and records

            names[done] = charc.Name;
            keys[done] = key;
            done++;
        }
        Array.Sort(names, keys);
        using var lookup = new StreamWriter(Path.Combine(dir, "lookup.txt"));
        for (int i = 0; i < done; i++)
            lookup.WriteLine($"{names[i]}  ->  {keys[i]}");
    }

    private JsonDocument? GetJson(string what)
    {
        var jsonPaths = files
            .Where(p => p.Key.StartsWith(what) && p.Key.EndsWith(".json"))
            .ToArray();
        if (jsonPaths.Length != 1)
        {
            CantLocate($"JSON-file of {what}");
            return null;
        }
        if (jsonPaths[0].Value.Count != 1)
        {
            CantLocate($"JSON-file of {what}");
            return null;
        }
        var stream = File.OpenRead(jsonPaths[0].Value.Items[0]);
        var json = JsonDocument.Parse(stream);
        stream.Dispose();
        return json;
    }

    static void CantLocate(string what)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Couldn't locate {what}.");
        Console.ResetColor();
    }
}

// Modules below
record ModuleDesc(string Name, string Icon, string Description, string Type, List<string> Missions, List<List<(string, int)>> Cost);

// Skins below
record SkinDesc(string AvatarId, string? PortraitId, string? Name, List<string> Authors, string? Group, string? Content, string? Dialog, string? Usage, string? Description);

// Skills below
record SkillLevelDesc(string Description, int SP, int InitSP, float Duration, Dictionary<string, string> Blackboard)
{
    public const float InfiniteDuration = -1f;

    public string FormatDescription()
    {
        var src = Description.AsSpan();
        var sb = new StringBuilder();
        while (src.Length > 0)
        {
            var left = src.IndexOf('{');
            if (left == -1)
            {
                sb.Append(src);
                src = default;
                continue;
            }
            if (left > 0)
            {
                sb.Append(src[..left]);
                src = src[left..];
                continue;
            }
            var right = src.IndexOf('}');
            var fmt = src[1..right];
            src = src[(right + 1)..];

            var sep = fmt.IndexOf(':');
            var fmtKey = (sep == -1) ? fmt : fmt[..sep];
            var fmtFmt = (sep == -1) ? default : fmt[(sep + 1)..];

            var sign = ((fmtKey.Length > 0) && (fmtKey[0] == '-')) ? -1 : 1;
            if (sign == -1) fmtKey = fmtKey[1..];

            var fmtVal = fmtKey.SequenceEqual("duration") ? Duration.ToString() : Blackboard[fmtKey.ToString().ToLower()].AsSpan();

            if (fmtFmt.Length == 0 || fmtFmt.SequenceEqual("0.0") || fmtFmt.SequenceEqual("0"))
            {
                if (float.TryParse(fmtVal, out var floatVal))
                    sb.Append(MathF.Round(floatVal * sign, 2));
                else
                    sb.Append(fmtVal);
            }
            else if (fmtFmt.SequenceEqual("0%") || fmtFmt.SequenceEqual("0.0%"))
                sb.Append($"{MathF.Round(float.Parse(fmtVal) * sign * 100, 2)}%");
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Unknown format string - {fmtFmt}");
                Console.ResetColor();
                sb.Append($"???{fmtVal}:{fmtFmt}???");
            }
        }
        return sb.ToString();
    }
}

record SkillDesc(string Name, string? IconId, string ActivationType, string ChargeType, string? RangeId, List<SkillLevelDesc> Levels);

// Items below
record ItemDesc(string Name, string? Description, string? Usage, string? Obtain, int? Tier,
    FrozenSet<string> IconPath, List<(string Stage, string? Chance)> Stages, List<(string Type, long Id)> Crafts);

// Stages below
record StageDropDesc(string Id, string? Chance, string? Type);

record StageDesc(string Code, string Name, string Description, string ZoneID, string UnitLevel, bool IsChallengeMode, string? Environment, int SanityCost, List<StageDropDesc> Drops)
{
    public string DisplayCode => Environment switch
    {
        "EASY" => $"{Code} Story",
        "NORMAL" => $"{Code} Standard",
        "TOUGH" => $"{Code} Adverse",
        "ALL" or null => $"{Code}{(IsChallengeMode ? " Challenge" : "")}",
        _ => throw new("Unexpected environment")
    };
}

// Characters below
record CharStats(int Level, int MaxHP, int ATK, int DEF, float RES, int DP, int Block, float AttackTime, int RespawnTime);

record CharEliteDesc(string RangeId, List<CharStats> Stats, List<(string, int)> Materials);

record CharSkillDesc(string Id, List<List<(string, int)>> MasteryCost);

record CharTalentDesc(int Index, int EliteNeed, int LevelNeed, int PotentialNeed, bool ByModule, string Name, string Description);

record TrustBonusDesc(int Level, int ATK, int DEF, int MaxHP);

record CharacterDesc(string Name, string? Description, string? TokenId, string? KernelTokenId, string Position,
    List<string> Tags, string? ItemUsage, string? ItemDesc, int Rarity, string Class, string SubClass,
    List<CharEliteDesc> Elites, List<CharSkillDesc> Skills, List<CharTalentDesc> Talents, List<string> Potentials,
    List<TrustBonusDesc> TrustBonuses, List<List<(string, int)>> SkillLevelCosts)
{
    public bool IsFake => SubClass.Contains("notchar");
}