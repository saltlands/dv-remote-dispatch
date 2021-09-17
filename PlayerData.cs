using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DvMod.RemoteDispatch
{
    public static class PlayerData
    {
        private static World.Position previousPosition;
        private static float previousRotation;

        public static void CheckTransform()
        {
            var transform = PlayerManager.PlayerTransform;
            if (transform == null)
                return;
            var position = new World.Position(transform.position - WorldMover.currentMove);
            var rotation = transform.eulerAngles.y;
            if (!(
                ApproximatelyEquals(previousPosition.x, position.x)
                && ApproximatelyEquals(previousPosition.z, position.z)
                && ApproximatelyEquals(previousRotation, rotation)))
            {
                Sessions.AddTag("player");
                previousPosition = position;
                previousRotation = rotation;
            }
        }

        private static bool ApproximatelyEquals(float f1, float f2)
        {
            var delta = f1 - f2;
            return delta > -1e-3 && delta < 1e-3;
        }

        public static JObject GetPlayerData()
        {
            CheckTransform();
            return new JObject(
                new JProperty("position", previousPosition.ToLatLon().ToJson()),
                new JProperty("rotation", Math.Round(previousRotation, 2)),
                // TODO: Only add licenses if they have changed since the last sync.
                new JProperty("licenses", licensesToJson())
            );
        }

        private static JObject licensesToJson()
        {
            int license = (int)LicenseManager.GetAcquiredJobLicenses();
            Type enumType = typeof(JobLicenses);
            JObject result = new JObject();
            foreach (int val in Enum.GetValues(enumType))
            {
                if (val > 0)
                {
                    result.Add(Enum.GetName(enumType, val), (license & val) > 0);
                }
            }
            return result;
        }

        public static string GetPlayerDataJson()
        {
            return JsonConvert.SerializeObject(GetPlayerData());
        }
    }
}