using System;
using System.Collections.Generic;

namespace GameInputLab.Core
{
    public static class PlaybackEventSelector
    {
        public static bool HasRelativeCameraData(IList<RecordedEvent> events)
        {
            if (events == null)
                return false;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] != null &&
                    events[i].Kind == RecordedEventKind.MouseRelativeMove)
                {
                    return true;
                }
            }
            return false;
        }

        public static List<RecordedEvent> SelectForPlayback(
            IList<RecordedEvent> events, bool useRelativeCamera)
        {
            List<RecordedEvent> result = new List<RecordedEvent>();
            if (events == null)
                return result;

            bool hasRawRelative = HasRelativeCameraData(events);
            if (useRelativeCamera && !hasRawRelative)
            {
                bool hasPreviousPoint = false;
                int previousX = 0;
                int previousY = 0;
                for (int i = 0; i < events.Count; i++)
                {
                    RecordedEvent item = events[i];
                    if (item == null)
                        continue;
                    if (item.Kind == RecordedEventKind.MouseMove)
                    {
                        if (hasPreviousPoint && (item.X != previousX || item.Y != previousY))
                        {
                            RecordedEvent derived = item.Clone();
                            derived.Kind = RecordedEventKind.MouseRelativeMove;
                            derived.X = item.X - previousX;
                            derived.Y = item.Y - previousY;
                            result.Add(derived);
                        }
                        previousX = item.X;
                        previousY = item.Y;
                        hasPreviousPoint = true;
                        continue;
                    }
                    if (item.Kind != RecordedEventKind.MouseRelativeMove)
                        result.Add(item.Clone());
                }
                return result;
            }

            for (int i = 0; i < events.Count; i++)
            {
                RecordedEvent item = events[i];
                if (item == null)
                    continue;
                if (useRelativeCamera && item.Kind == RecordedEventKind.MouseMove)
                    continue;
                if (!useRelativeCamera && item.Kind == RecordedEventKind.MouseRelativeMove)
                    continue;
                result.Add(item.Clone());
            }
            return result;
        }
    }
}
