using System.Collections;
using System.Reflection;
using Arch.CommandBuffer;
using Arch.Core;
using Arch.Core.Extensions;
using Zinc.Core;

namespace MDH;

public static class Logic
{
    public class LogicBindingAttribute : System.Attribute
    {
        public string LogicID { get; protected set; }
        public LogicBindingAttribute(MetaEvents m) : this(m.ToString()){}
        public LogicBindingAttribute(string logicID)
        {
            LogicID = logicID;
        }
    }

    public static Dictionary<string, MethodInfo> LogicBindingDict = new Dictionary<string, MethodInfo>();
    public static void InitBindings()
    {
        // Get all methods in the current class
        foreach (var method in typeof(Logic).GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        {
            // Check if the method has the KeywordBinding attribute
            var attribute = method.GetCustomAttribute<LogicBindingAttribute>();
            if (attribute != null && method.IsStatic && method.ReturnType == typeof(IEnumerator))
            {
                LogicBindingDict.Add(attribute.LogicID, method);
            }
        }
    }

    public enum MetaEvents
    {
        PlayerAttacking,
        CardsActing,
        DeathReap,
        DrawingMultipleCards,
        UpdateCardPositions,
        UpdateAndFillTrack
    }

    public static Systems.Logic.Event Emit(
        this MetaEvents m,
        Systems.Logic.Event parent,
        Systems.Logic.EventData? data = null,
        Action<Systems.Logic.Event> postExecution = null, Action onComplete = null)
    {
        if (LogicBindingDict.TryGetValue(m.ToString(), out MethodInfo methodInfo))
        {
            var newEvent =  new Systems.Logic.Event(m.ToString(), data, (IEnumerator)methodInfo.Invoke(null, [Systems.Logic.GetCurrentEventCounter(),data]),postExecution, onComplete);
            parent.ChildEvents.Add(newEvent);
            return newEvent;
        }
        
        throw new KeyNotFoundException($"No method bound to logic '{m.ToString()}'.");
    }

    public static Systems.Logic.Event Emit(
        this Depot.Generated.dungeon.logicTriggers.logicTriggersLine logicEvent,
        Systems.Logic.Event parent,
        Systems.Logic.EventData? data = null,
        Action<Systems.Logic.Event> postExecution = null, Action onComplete = null)
    {
        if (LogicBindingDict.TryGetValue(logicEvent.ID, out MethodInfo methodInfo))
        {
            //attach pre-events to the parent so they execute before the main phase of this event
            foreach (var trackCard in Dungeon.Track.Cards.Where(x => x.Value != null))
            {
                foreach (var keyword in trackCard.Value!.Keywords)
                {
                    if (keyword.trigger.trigger == logicEvent &&
                        keyword.trigger.phase == Depot.Generated.dungeon.keywords.triggerProps.phase_ENUM.pre &&
                        ValidateKeywordTriggerTarget(trackCard.Value!, keyword, data)
                       )
                    {
                        keyword.Emit(parent,data);
                    }
                }
            }
            foreach (var invCard in Dungeon.Inventory.Cards.Where(x => x.Value != null))
            {
                foreach (var keyword in invCard.Value!.Keywords)
                {
                    if (keyword.trigger.trigger == logicEvent &&
                        keyword.trigger.phase == Depot.Generated.dungeon.keywords.triggerProps.phase_ENUM.pre &&
                        ValidateKeywordTriggerTarget(invCard.Value!, keyword, data)
                       )
                    {
                        keyword.Emit(parent,data);
                    }
                }
            }
            
            var main = new Systems.Logic.Event(logicEvent.ID, data,(IEnumerator)methodInfo.Invoke(null, [Systems.Logic.GetCurrentEventCounter(),data]), 
                e =>
            {
                //attach post-events to our event
                foreach (var trackCard in Dungeon.Track.Cards.Where(x => x.Value != null && x.Value.Health > 0))
                {
                    foreach (var keyword in trackCard.Value!.Keywords)
                    {
                        if (keyword.trigger.trigger == logicEvent &&
                            keyword.trigger.phase == Depot.Generated.dungeon.keywords.triggerProps.phase_ENUM.post &&
                            ValidateKeywordTriggerTarget(trackCard.Value!, keyword, data)
                           )
                        {
                            keyword.Emit(e,data);
                        }
                    }
                }
                foreach (var invCard in Dungeon.Inventory.Cards.Where(x => x.Value != null))
                {
                    foreach (var keyword in invCard.Value!.Keywords)
                    {
                        if (keyword.trigger.trigger == logicEvent &&
                            keyword.trigger.phase == Depot.Generated.dungeon.keywords.triggerProps.phase_ENUM.post &&
                            ValidateKeywordTriggerTarget(invCard.Value!, keyword, data)
                           )
                        {
                            keyword.Emit(parent,data);
                        }
                    }
                }
                
                postExecution?.Invoke(e);
            }, onComplete);
            
            parent.ChildEvents.Add(main);
            

            return main;
        }

        throw new KeyNotFoundException($"No method bound to logic '{logicEvent.ID}'.");

        bool ValidateKeywordTriggerTarget(
            DeckCard triggeringCard,
            Depot.Generated.dungeon.keywords.keywordsLine triggeringKeyword,
            Systems.Logic.EventData? data)
        {
            if (data == null)
            {
                return true;
            }

            return triggeringKeyword.trigger.target.ToString() switch
            {
                "self" => triggeringCard.ID == data.cardID,
                "any" => true,
                _ => false
            };
        }
    }
    
    /*
     * LOGIC BINDINGS-------------------------------------------------------------------
     *
     *
     *
     *
     *
     *
     *
     *
     * 
     */

    [LogicBinding("move")]
    public static IEnumerator Move(int eventID, Systems.Logic.EventData? d)
    {
        int moveDist = 1; //saturate this with status/buffs/etc
        foreach (var c in Dungeon.Track.Cards.Where(x => x.Value != null))
        {
            c.Value!.Distance += moveDist;
        }
        Dungeon.Player.MovedDistance += moveDist;
        // Health = Health + 1 < MaxHealth ? Health + 1 : Health; moving this to keyword
        Dungeon.Player.Fullness--;
        if (Dungeon.Player.Fullness <= 0)
        {
            Dungeon.Player.Kill();
        }
        yield return null;
    }
    [LogicBinding("wait")]
    public static IEnumerator Wait(int eventID, Systems.Logic.EventData? d)
    {
        Dungeon.Player.Fullness--;
        if (Dungeon.Player.Fullness <= 0)
        {
            Dungeon.Player.Kill();
        }
        yield return null;
    }
    [LogicBinding("draw")]
    public static IEnumerator Draw(int eventID, Systems.Logic.EventData? d)
    {
        var callingEvent = Systems.Logic.FindEventWithID(eventID);
        var hasOpenSpot = Dungeon.Track.Cards.Any(x => x.Value == null);
        if (Dungeon.Deck.Cards.Any() && hasOpenSpot)
        {
            var nextDraw = Dungeon.Deck.Cards.First();
            var targetPos = Dungeon.Track.Cards.FirstOrDefault(x => x.Value == null).Key;
            Dungeon.Track.Cards[targetPos] = nextDraw;
            nextDraw.Distance = 3;
            nextDraw.Entity.Active = true;
            Dungeon.Deck.Cards.Remove(nextDraw);
            yield return null;
        }
        else
        {
            callingEvent.Value.self.Cancelled = true;
            yield return null;
        }
    }
    [LogicBinding("discard")]
    public static IEnumerator Discard(int eventID, Systems.Logic.EventData? d)
    {
        //NOTE: we assume that discard is only used for cards in the track
        var card = Dungeon.AllCards[d.cardID];
        var callingEvent = Systems.Logic.FindEventWithID(eventID);

        if (Dungeon.Track.ContainsCard(card))
        {
            Dungeon.DiscardStack.Add(card);
            Dungeon.Track.RemoveTrackCard(card);
        }
        else
        {
            callingEvent.Value.self.Cancelled = true;
            //discard is no longer valid

        }
        yield return null;
    }
    [LogicBinding("attackedByPlayer")]
    public static IEnumerator AttackedByPlayer(int eventID, Systems.Logic.EventData? d)
    {
        var card = Dungeon.AllCards[d.cardID];
        yield return Dungeon.Effects.Shake(card);
        card.Health -= 1;
        yield return null;
    }
    
    [LogicBinding("use")]
    public static IEnumerator Use(int eventID, Systems.Logic.EventData? d)
    {
        var card = Dungeon.AllCards[d.cardID];
        card.Entity.Active = false;
        Dungeon.Graveyard.Add(card);
        
        Dungeon.Inventory.RemoveFromInventory(card); //right now use only works in inventory
        yield return null;
    }
    
    [LogicBinding("destroyed")]
    public static IEnumerator Destroyed(int eventID, Systems.Logic.EventData? d)
    {
        var card = Dungeon.AllCards[d.cardID];
        card.Entity.Active = false;
        Dungeon.Graveyard.Add(card);
        if (Dungeon.Track.Cards.ContainsValue(card))
        {
            var trackPos = Dungeon.Track.Cards.First(x => x.Value == card).Key;
            Dungeon.Track.RemoveTrackCard(card);
            
            //add loot drop
            if(card.LootTable != null && card.LootTable.GetNextDrop(out var droppedCard))
            {
                Dungeon.Track.Cards[trackPos] = droppedCard;
                droppedCard.Distance = 3;
                droppedCard.Entity.Active = true;
            }
            
            // yield return Dungeon.Track.MoveTrackCardsToLatestTrackPositions();
        }
        yield return null;
    }
    [LogicBinding("attacking")]
    public static IEnumerator Attacking(int eventID, Systems.Logic.EventData? d)
    {
        var card = Dungeon.AllCards[d.cardID];
        float startY = card.Entity.Y;
        float endY = card.Entity.Y + 80;

        var trans = new Transition<float>(startY, endY, Easing.Option.EaseOutElastic);
        TimeSince ts = 0;
        while (ts < 0.2f)
        {
            card.Entity.Y = startY + ((endY - startY) * (float)trans.Sample(ts / 0.2f));
            yield return null;
        }
        //damage player
        if (!Dungeon.ActiveDebugOptions.Invincible)
        {
            Dungeon.Player.Health -= card.Attack;
        }
        //done damaging player
        ts = 0;
        trans = new Transition<float>(startY, endY, Easing.Option.EaseOutExpo);
        while (ts < 0.12f)
        {
            card.Entity.Y = endY + ((startY - endY) * (float)trans.Sample(ts / 0.12f));
            yield return null;
        }
        yield return null;
    }
    
    [LogicBinding(MetaEvents.PlayerAttacking)]
    public static IEnumerator PlayerAttacking(int eventID, Systems.Logic.EventData? d)
    {
        yield return null;
    }
    
    [LogicBinding(MetaEvents.CardsActing)]
    public static IEnumerator CardsActing(int eventID, Systems.Logic.EventData? d)
    {
        yield return null;
    }
    
    [LogicBinding(MetaEvents.DeathReap)]
    public static IEnumerator DeathReap(int eventID, Systems.Logic.EventData? d)
    {
        yield return null;
    }
    
    [LogicBinding(MetaEvents.DrawingMultipleCards)]
    public static IEnumerator DrawingMultipleCards(int eventID, Systems.Logic.EventData? d)
    {
        yield return null;
    }
    
    [LogicBinding(MetaEvents.UpdateAndFillTrack)]
    public static IEnumerator UpdateAndFillTrack(int eventID, Systems.Logic.EventData? d)
    {
        var callingEvent = Systems.Logic.FindEventWithID(eventID);
        MetaEvents.UpdateCardPositions.Emit(callingEvent.Value.self, postExecution: updateCardPos =>
        {
            var openSpots = Dungeon.Track.Cards.Count(x => x.Value == null);
            for (int i = 0; i < openSpots; i++)
            {
                Depot.Generated.dungeon.logicTriggers.draw.Emit(updateCardPos,postExecution: drawEvent =>
                {
                    MetaEvents.UpdateCardPositions.Emit(drawEvent);
                });
            }
        });
        yield return null;
    }
    
    [LogicBinding(MetaEvents.UpdateCardPositions)]
    public static IEnumerator UpdateCardPositions(int eventID, Systems.Logic.EventData? d)
    {
        yield return Dungeon.Track.MoveTrackCardsToLatestTrackPositions();
    }
}
