using DV.Logic.Job;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DvMod.RemoteDispatch
{
    public static class JobData
    {
        private static readonly Dictionary<TrainCar, string> jobIdForCar = InitializeJobIdForCar();
        private static Dictionary<string, Job> jobForId = new Dictionary<string, Job>();

        public static string? JobIdForCar(TrainCar car)
        {
            jobIdForCar.TryGetValue(car, out var jobId);
            return jobId;
        }

        public static Job? JobForCar(TrainCar car)
        {
            var jobId = JobIdForCar(car);
            if (jobId == null)
                return null;
            return JobForId(jobId);
        }

        private static Dictionary<TrainCar, string> InitializeJobIdForCar()
        {
            return JobChainController.jobToJobCars
                .SelectMany(kvp => kvp.Value.Select(car => (car, job: kvp.Key)))
                .ToDictionary(p => p.car, p => p.job.ID);
        }

        public static Job? JobForId(string jobId)
        {
            if (jobForId.TryGetValue(jobId, out var job))
                return job;
            jobForId = JobChainController.jobToJobCars.Keys.ToDictionary(job => job.ID);
            jobForId.TryGetValue(jobId, out job);
            return job;
        }

        // LicensesToJson converts the JobLicenses enum into a json array of the enum's names.
        private static JArray LicensesToJson(JobLicenses data)
        {
            Type enumType = typeof(JobLicenses);
            int license = (int)data;
            JArray result = new JArray();
            foreach (int val in Enum.GetValues(enumType)) {
                if ((license & val) > 0)
                {
                    result.Add(Enum.GetName(enumType, val));
                }
            }
            return result;
        }

        public static Dictionary<string, JObject> GetAllJobData()
        {
            static IEnumerable<TaskData> FlattenToTransport(TaskData data)
            {
                if (data.type == TaskType.Transport)
                {
                    yield return data;
                }
                else if (data.nestedTasks != null)
                {
                    foreach (var nested in data.nestedTasks)
                    {
                        foreach (var task in FlattenToTransport(nested.GetTaskData()))
                            yield return task;
                    }
                }
            }
            static IEnumerable<TaskData> FlattenMany(IEnumerable<TaskData> data) => data.SelectMany(FlattenToTransport);
            static JObject TaskToJson(TaskData data) => new JObject(
                new JProperty("startTrack", data.startTrack?.ID?.FullDisplayID),
                new JProperty("destinationTrack", data.destinationTrack?.ID?.FullDisplayID),
                new JProperty("cars", (data.cars ?? new List<Car>()).Select(car => car.ID))
            );
            static JObject JobToJson(Job job) => new JObject(
                new JProperty("originYardId", job.chainData.chainOriginYardId),
                new JProperty("destinationYardId", job.chainData.chainDestinationYardId),
                new JProperty("tasks", FlattenMany(job.GetJobData()).Select(TaskToJson)),
                new JProperty("licenses", LicensesToJson(job.requiredLicenses)),
                new JProperty("payment", job.GetBasePaymentForTheJob()),
                new JProperty("bonusPayment", job.GetBonusPaymentForTheJob()),
                new JProperty("bonusTime", job.TimeLimit),
                new JProperty("startTime", job.startTime),
                new JProperty("elapsedTime", job.GetTimeOnJob()),
                new JProperty("trainWeight", FlattenMany(job.GetJobData()).Sum(j => j.cars == null ? 0 : j.cars.Sum(c => c.carOnlyMass + CargoTypes.cargoTypeToCargoMassPerUnit[c.CurrentCargoTypeInCar])) / (job.jobType == JobType.ShuntingLoad || job.jobType == JobType.ShuntingUnload ? 2 : 1)),
                new JProperty("trainLength", FlattenMany(job.GetJobData()).Sum(j => j.cars == null ? 0 : j.cars.Sum(c => c.length)) / (job.jobType == JobType.ShuntingLoad || job.jobType == JobType.ShuntingUnload ? 2 : 1)),
                new JProperty("trainCars", FlattenMany(job.GetJobData()).Sum(j => j.cars == null ? 0 : j.cars.Count()) / (job.jobType == JobType.ShuntingLoad || job.jobType == JobType.ShuntingUnload ? 2 : 1))
            );

            // ensure cache is updated
            JobForId("");
            return jobForId.ToDictionary(
                kvp => kvp.Key,
                kvp => JobToJson(kvp.Value)
            );
        }

        public static string GetAllJobDataJson()
        {
            return JsonConvert.SerializeObject(GetAllJobData());
        }

        public static class JobPatches
        {
            [HarmonyPatch(typeof(JobChainController), nameof(JobChainController.UpdateTrainCarPlatesOfCarsOnJob))]
            public static class UpdateTrainCarPlatesOfCarsOnJobPatch
            {
                public static void Postfix(JobChainController __instance, string jobId)
                {
                    foreach (TrainCar car in __instance.trainCarsForJobChain)
                    {
                        if (jobId.Length == 0)
                            jobIdForCar.Remove(car);
                        else
                            jobIdForCar[car] = jobId;
                        Sessions.AddTag("jobs");
                    }
                }
            }

            [HarmonyPatch(typeof(JobBooklet), nameof(JobBooklet.Awake))]
            public static class UpdateJobBooklet
            {
                public static void Postfix(JobBooklet __instance)
                {
                    Sessions.AddTag("jobs");
                }
            }
        }
    }
}