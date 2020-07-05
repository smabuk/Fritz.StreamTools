﻿using Fritz.StreamLib.Core;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.Models;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training;
using System.Net;

namespace Fritz.Chatbot.Commands
{
	public class PredictHatCommand : IBasicCommand
	{
		public string Trigger => "hat";
		public string Description => "Identify which hat Fritz is wearing";
		public TimeSpan? Cooldown => TimeSpan.FromSeconds(30);

		private string _CustomVisionKey = "";
		private string _AzureEndpoint = "";
		private string _TwitchChannel = "";
		private Guid _AzureProjectId;

		internal static string IterationName = "";
		private ScreenshotTrainingService _TrainHat;
		private readonly HatDescriptionRepository _Repository;

		public PredictHatCommand(IConfiguration configuration, ScreenshotTrainingService service, HatDescriptionRepository repository)
		{
			_CustomVisionKey = configuration["AzureServices:HatDetection:Key"];
			_AzureEndpoint = configuration["AzureServices:HatDetection:CustomVisionEndpoint"];
			_TwitchChannel = configuration["StreamServices:Twitch:Channel"];
			_AzureProjectId = Guid.Parse(configuration["AzureServices:HatDetection:ProjectId"]);
			_TrainHat = service;
			_Repository = repository;
		}

		public string TwitchScreenshotUrl => $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{_TwitchChannel}-1280x720.jpg?_=";


		public async Task Execute(IChatService chatService, string userName, ReadOnlyMemory<char> rhs)
		{

			if (string.IsNullOrEmpty(IterationName)) {
				await IdentifyIterationName();
			}

			var client = new CustomVisionPredictionClient()
			{
				ApiKey = _CustomVisionKey,
				Endpoint = _AzureEndpoint
			};

			var obsImage = await _TrainHat.GetScreenshotFromObs();

			////////////////////////////

			ImagePrediction result;
			try
			{
				result = await client.DetectImageWithNoStoreAsync(_AzureProjectId, IterationName, obsImage);
			} catch (CustomVisionErrorException ex) {

				

				if (ex.Response.StatusCode == HttpStatusCode.NotFound) {
					await IdentifyIterationName();
				}

				await chatService.SendMessageAsync("Unable to detect Fritz's hat right now... please try again in 1 minute");
				return;

			}


			var bestMatch = result.Predictions.OrderByDescending(p => p.Probability).FirstOrDefault();
			if (bestMatch == null || bestMatch.Probability <= 0.3D) {
				await chatService.SendMessageAsync("csharpAngry 404 Hat Not Found!  Let's ask a moderator to !addhat so we can identify it next time");
				// do we store the image?
				return;
			}

			await chatService.SendMessageAsync($"csharpClip I think (with {bestMatch.Probability.ToString("0.0%")} certainty) Jeff is currently wearing his {bestMatch.TagName} hat csharpClip");
			if (bestMatch.Probability >= 0.6D) {
				var desc = await _Repository.GetDescription(bestMatch.TagName);
				if (!string.IsNullOrEmpty(desc)) await chatService.SendMessageAsync(desc);
			}

		}

		private async Task IdentifyIterationName()
		{

			var client = new CustomVisionTrainingClient() { 
				ApiKey = _CustomVisionKey,
				Endpoint = _AzureEndpoint
			};

			var iterations = await client.GetIterationsAsync(_AzureProjectId);
			IterationName = iterations
				.Where(i => !string.IsNullOrEmpty(i.PublishName) && i.Status == "Completed")
				.OrderByDescending(i => i.LastModified).First().PublishName;

		}
	}
}
