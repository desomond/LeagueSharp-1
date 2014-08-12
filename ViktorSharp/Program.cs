﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;

namespace ViktorSharp
{
    class Program
    {
        // Generic
        private static readonly string champName = "Viktor";
        private static readonly Obj_AI_Hero player = ObjectManager.Player;

        // Spells
        private static readonly List<Spell> spellList = new List<Spell>();
        private static Spell Q, W, E, R;
        private static readonly int maxRangeE = 1200;
        private static readonly int lengthE   = 750;
        private static readonly int speedE    = 780;
        private static readonly int rangeE    = 540;

        // Menu
        private static Menu menu;

        private static Orbwalking.Orbwalker OW;

        public static void Main(string[] args)
        {
            // Register events
            CustomEvents.Game.OnGameLoad += Game_OnGameLoad;
        }

        private static void Game_OnGameLoad(EventArgs args)
        {
            // Champ validation
            if (player.ChampionName != champName) return;
            
            // Define spells
            Q = new Spell(SpellSlot.Q, 600);
            W = new Spell(SpellSlot.W, 625);
            E = new Spell(SpellSlot.E, rangeE);
            R = new Spell(SpellSlot.R, 600);
            spellList.AddRange(new []{Q, W, E, R});

            // Finetune spells
            Q.SetTargetted(0.25f, 1400f);
            W.SetSkillshot(0.25f, 300f, float.MaxValue, false, Prediction.SkillshotType.SkillshotCircle);
            E.SetSkillshot(0f,    80f,  speedE,         false, Prediction.SkillshotType.SkillshotLine);
            R.SetSkillshot(0f,    450f, float.MaxValue, false, Prediction.SkillshotType.SkillshotCircle);

            // Create menu
            createMenu();

            // Register events
            Game.OnGameUpdate += Game_OnGameUpdate;
        }

        private static void Game_OnGameUpdate(EventArgs args)
        {
            if (menu.SubMenu("combo").Item("active").GetValue<KeyBind>().Active)
                OnCombo();
        }

        private static void OnCombo()
        {
            Menu comboMenu = menu.SubMenu("combo");
            bool useQ = comboMenu.Item("useQ").GetValue<bool>() && Q.IsReady();
            //bool useW = comboMenu.Item("useW").GetValue<bool>() && W.IsReady();
            bool useE = comboMenu.Item("useE").GetValue<bool>() && E.IsReady();
            //bool useR = comboMenu.Item("useR").GetValue<bool>() && R.IsReady();
            bool longRange = comboMenu.Item("extend").GetValue<KeyBind>().Active;

            if (useQ)
            {
                var target = SimpleTs.GetTarget(Q.Range, SimpleTs.DamageType.Magical);
                if (target != null)
                    Q.Cast(target);
            }

            if (useE)
            {
                var target = SimpleTs.GetTarget(longRange ? maxRangeE : E.Range, SimpleTs.DamageType.Magical);
                if (target != null)
                    predictCastE(target, longRange);
            }
        }

        private static bool predictCastMinionE()
        {
            int hitNum = 0;
            Vector2 startPos = new Vector2(0, 0);
            foreach(var minion in MinionManager.GetMinions(player.Position, rangeE))
            {
                var farmLocation = MinionManager.GetBestLineFarmLocation((from mnion in MinionManager.GetMinions(minion.Position, lengthE) select mnion.Position.To2D()).ToList<Vector2>(), E.Width, lengthE);
                if (hitNum == 0 || farmLocation.MinionsHit > hitNum)
                {
                    hitNum = farmLocation.MinionsHit;
                    startPos = minion.Position.To2D();
                }
            }

            if (startPos.X != 0 && startPos.Y != 0)
                return predictCastMinionE(startPos);

            return false;
        }

        private static bool predictCastMinionE(Vector2 fromPosition)
        {
            var farmLocation = MinionManager.GetBestLineFarmLocation(MinionManager.GetMinionsPredictedPositions(MinionManager.GetMinions(fromPosition.To3D(), lengthE), E.Delay, E.Width, speedE, fromPosition.To3D(), lengthE, false, Prediction.SkillshotType.SkillshotLine), E.Width, lengthE);

            if (farmLocation.MinionsHit > 0)
            {
                castE(fromPosition, farmLocation.Position);
                return true;
            }

            return false;
        }

        private static void predictCastE(Obj_AI_Hero target, bool longRange = false)
        {
            // Helpers
            bool inRange = player.Distance(target) < E.Range;
            Prediction.PredictionOutput prediction;
            bool spellCasted = false;

            // Positions
            Vector3 pos1, pos2;

            // Champs
            var nearChamps = (from champ in ObjectManager.Get<Obj_AI_Hero>() where champ.IsValidTarget(maxRangeE) && target != champ select champ).ToList();
            var innerChamps = new List<Obj_AI_Hero>();
            var outerChamps = new List<Obj_AI_Hero>();
            foreach (var champ in nearChamps)
            {
                if (Vector2.DistanceSquared(champ.ServerPosition.To2D(), player.Position.To2D()) < E.Range * E.Range)
                    innerChamps.Add(champ);
                else
                    outerChamps.Add(champ);
            }

            // Minions
            var nearMinions = (from minion in ObjectManager.Get<Obj_AI_Minion>() where minion.IsValidTarget(maxRangeE) select minion).ToList();
            var innerMinions = new List<Obj_AI_Minion>();
            var outerMinions = new List<Obj_AI_Minion>();
            foreach (var minion in nearMinions)
            {
                if (Vector2.DistanceSquared(minion.ServerPosition.To2D(), player.Position.To2D()) < E.Range * E.Range)
                    innerMinions.Add(minion);
                else
                    outerMinions.Add(minion);
            }

            // Main target in close range
            if (inRange)
            {
                // Get prediction reduced speed, adjusted sourcePosition
                E.Speed = speedE * 0.9f;
                E.From = target.ServerPosition + (Vector3.Normalize(player.Position - target.ServerPosition) * (lengthE * 0.1f));
                prediction = E.GetPrediction(target);
                E.From = player.Position;

                // Prediction in range, go on
                if (prediction.CastPosition.Distance(player.Position) < E.Range)
                    pos1 = prediction.CastPosition;
                // Prediction not in range, use exact position
                else
                {
                    pos1 = target.ServerPosition;
                    E.Speed = speedE;
                }

                // Set new sourcePosition
                E.From = pos1;

                // Set new range
                E.Range = lengthE;

                // Get next target
                if (nearChamps.Count > 0)
                {
                    // Get best champion around
                    var closeToPrediction = new List<Obj_AI_Hero>();
                    foreach (var enemy in nearChamps)
                    {
                        // Get prediction
                        prediction = E.GetPrediction(enemy);
                        // Validate target
                        if (prediction.HitChance == Prediction.HitChance.HighHitchance && Vector2.DistanceSquared(pos1.To2D(), prediction.CastPosition.To2D()) < (E.Range * E.Range) * 0.8)
                            closeToPrediction.Add(enemy);
                    }

                    // Champ found
                    if (closeToPrediction.Count > 0)
                    {
                        // Sort table by health DEC
                        if (closeToPrediction.Count > 1)
                            closeToPrediction.Sort((enemy1, enemy2) => enemy2.Health.CompareTo(enemy1.Health));

                        // Set destination
                        prediction = E.GetPrediction(closeToPrediction[0]);
                        pos2 = prediction.CastPosition;

                        // Cast spell
                        castE(pos1, pos2);
                        spellCasted = true;
                    }
                }

                // Spell not casted
                if (!spellCasted)
                    // Try casting on minion
                    if (!predictCastMinionE(pos1.To2D()))
                        // Cast it directly
                        castE(pos1, E.GetPrediction(target).CastPosition);

                // Reset spell
                E.Speed = speedE;
                E.Range = rangeE;
                E.From  = player.Position;
            }

            // Main target in extended range
            else
            {
                // Radius of the start point to search enemies in
                float startPointRadius = 150;

                // Get initial start point at the border of cast radius
                Vector3 startPoint = player.Position + Vector3.Normalize(target.ServerPosition - player.Position) * E.Range;

                // Potential start from postitions
                var targets = (from champ in nearChamps where Vector2.DistanceSquared(champ.ServerPosition.To2D(), startPoint.To2D()) < startPointRadius * startPointRadius && Vector2.DistanceSquared(player.Position.To2D(), champ.ServerPosition.To2D()) < rangeE * rangeE select champ).ToList<Obj_AI_Hero>();
                if (targets.Count > 0)
                {
                    // Sort table by health DEC
                    if (targets.Count > 1)
                        targets.Sort((enemy1, enemy2) => enemy2.Health.CompareTo(enemy1.Health));

                    // Set target
                    pos1 = targets[0].ServerPosition;
                }
                else
                {
                    var minionTargets = (from minion in nearMinions where Vector2.DistanceSquared(minion.ServerPosition.To2D(), startPoint.To2D()) < startPointRadius * startPointRadius && Vector2.DistanceSquared(player.Position.To2D(), minion.ServerPosition.To2D()) < rangeE * rangeE select minion).ToList<Obj_AI_Minion>();
                    if (minionTargets.Count > 0)
                    {
                        // Sort table by health DEC
                        if (minionTargets.Count > 1)
                            minionTargets.Sort((enemy1, enemy2) => enemy2.Health.CompareTo(enemy1.Health));

                        // Set target
                        pos1 = minionTargets[0].ServerPosition;
                    }
                    else
                        // Just the regular, calculated start pos
                        pos1 = startPoint;
                }

                // Predict target position
                E.From = pos1;
                E.Range = lengthE;
                prediction = E.GetPrediction(target);

                // Cast the E
                if (prediction.HitChance == Prediction.HitChance.HighHitchance)
                    castE(pos1, prediction.Position);

                // Reset spell
                E.Range = rangeE;
                E.From = player.Position;
            }

        }

        private static void castE(Vector3 source, Vector3 destination)
        {
            castE(source.To2D(), destination.To2D());
        }

        private static void castE(Vector2 source, Vector2 destination)
        {
            Packet.C2S.Cast.Encoded(new Packet.C2S.Cast.Struct(0, E.Slot, -1, source.X, source.Y, destination.X, destination.Y)).Send();
        }

        private static void createMenu()
        {
            menu = new Menu("[Hellsing] " + champName, "hells" + champName, true);

            // Target selector
            Menu ts = new Menu("Target Selector", "ts");
                menu.AddSubMenu(ts);
                SimpleTs.AddToMenu(ts);

            // Orbwalker
            Menu orbwalk = new Menu("Orbwalking", "orbwalk");
                menu.AddSubMenu(orbwalk);
                OW = new Orbwalking.Orbwalker(orbwalk);

            // Combo
            Menu combo = new Menu("Combo", "combo");
                menu.AddSubMenu(combo);
                combo.AddItem(new MenuItem("useQ",      "Use Q").SetValue(true));
                //combo.AddItem(new MenuItem("useW",      "Use W").SetValue(true));
                combo.AddItem(new MenuItem("useE",      "Use E").SetValue(true));
                //combo.AddItem(new MenuItem("useR",      "Use R").SetValue(true));
                combo.AddItem(new MenuItem("active",    "Combo active!").SetValue(new KeyBind(32, KeyBindType.Press)));
                combo.AddItem(new MenuItem("extend",    "E extended range!").SetValue(new KeyBind('A', KeyBindType.Press)));

            // Drawings
            //menu.AddSubMenu(new Menu("Drawings", "drawings"));

            // Finalizing
            menu.AddToMainMenu();
        }
    }
}