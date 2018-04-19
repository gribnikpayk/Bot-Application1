using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;
using Newtonsoft.Json;

namespace Bot_Application.Dialogs
{
    [Serializable]
    public class UKS_Dialog : IDialog<object>
    {
        private List<string> _conversationInfoSerializableList = new List<string>();
        private static float _delayInMinutes = 5f;
        private static Dictionary<string, string> _monitoringDictionary = new Dictionary<string, string>();
        private static HttpClient _client = new HttpClient();

        #region constants commands
        private const string CMD_ADD = "~add";
        private const string CMD_REMOVE = "~remove";
        private const string CMD_List = "~list";
        private const string CMD_CMD_List = "~CMD_list";
        private const string CMD_Settings = "~settings";
        private const string CMD_SetDelay = "~set_delay";
        private const string CMD_ShowRecipientList = "~recipient_list";
        #endregion
        public Task StartAsync(IDialogContext context)
        {
            context.PostAsync(CMD_CMD_List);
            Task.Run(RepeatBackgroundJob);

            context.Wait(MessageReceivedAsync);

            return Task.CompletedTask;
        }

        private async Task MessageReceivedAsync(IDialogContext context, IAwaitable<object> result)
        {
            var activity = await result as Activity;
            var message = (Activity) await result;

            AddNewConversationInfo(new ConversationInfo
            {
                toId = message.From.Id,
                toName = message.From.Name,
                fromId = message.Recipient.Id,
                fromName = message.Recipient.Name,
                serviceUrl = message.ServiceUrl,
                channelId = message.ChannelId,
                conversationId = message.Conversation.Id
            });

            string text = activity.Text ?? string.Empty;

            if (!string.IsNullOrEmpty(text))
            {
                var command = GetCommand(text);
                await RunCommand(command, text, context);
            }

            context.Wait(MessageReceivedAsync);
        }

        #region Commands
        private string GetCommand(string text)
        {
            var wordArr = text.Split(' ');
            return wordArr.FirstOrDefault(w => w.IndexOf("~") >= 0);
        }

        private async Task RunCommand(string command, string text, IDialogContext context)
        {
            switch (command)
            {
                case CMD_ADD:
                    await AddUrlToMonitor(context, text);
                    break;
                case CMD_List:
                    await ShowUrlList(context);
                    break;
                case CMD_REMOVE:
                    await RemoveUrl(context, text);
                    break;
                case CMD_CMD_List:
                    await ShowCommands(context);
                    break;
                case CMD_Settings:
                    await ShowSettings(context);
                    break;
                case CMD_SetDelay:
                    await ResetDelay(text, context);
                    break;
                case CMD_ShowRecipientList:
                    await ShowRecipientList(context);
                    break;
                default:
                    await UndefinedCommand(context, command);
                    break;
            }
        }

        private async Task notification()
        {
            //GetSiteData(null,
            //    "https://docs.microsoft.com/en-us/aspnet/web-api/overview/advanced/calling-a-web-api-from-a-net-client");


            foreach (var conversationInfoSerializable in _conversationInfoSerializableList)
            {
                var conversationInfo = JsonConvert.DeserializeObject<ConversationInfo>(conversationInfoSerializable);

                var userAccount = new ChannelAccount(conversationInfo.toId, conversationInfo.toName);
                var botAccount = new ChannelAccount(conversationInfo.fromId, conversationInfo.fromName);
                var connector = new ConnectorClient(new Uri(conversationInfo.serviceUrl));

                // Create a new message.
                IMessageActivity message = Activity.CreateMessageActivity();
                if (!string.IsNullOrEmpty(conversationInfo.conversationId) && !string.IsNullOrEmpty(conversationInfo.channelId))
                {
                    // If conversation ID and channel ID was stored previously, use it.
                    message.ChannelId = conversationInfo.channelId;
                }
                else
                {
                    // Conversation ID was not stored previously, so create a conversation. 
                    // Note: If the user has an existing conversation in a channel, this will likely create a new conversation window.
                    conversationInfo.conversationId = (await connector.Conversations.CreateDirectConversationAsync(botAccount, userAccount)).Id;
                }

                // Set the address-related properties in the message and send the message.
                message.From = botAccount;
                message.Recipient = userAccount;
                message.Conversation = new ConversationAccount(id: conversationInfo.conversationId);
                message.Text = "Hello, this is a notification";
                message.Locale = "en-us";
                await connector.Conversations.SendToConversationAsync((Activity)message);
            }
        }

        private async Task ShowCommands(IDialogContext context)
        {
            await context.PostAsync(CMD_ADD);
            await context.PostAsync(CMD_List);
            await context.PostAsync(CMD_REMOVE);
            await context.PostAsync(CMD_CMD_List);
            await context.PostAsync(CMD_Settings);
            await context.PostAsync(CMD_SetDelay);
            await context.PostAsync(CMD_ShowRecipientList);
        }

        private async Task ShowSettings(IDialogContext context)
        {
            await context.PostAsync($"SETTINGS: \n delay (min): {_delayInMinutes}\n");
        }

        private async Task UndefinedCommand(IDialogContext context, string cmd)
        {
            await context.PostAsync($"cmd '{cmd}' not found");
        }

        private async Task ShowRecipientList(IDialogContext context)
        {
            await context.PostAsync(String.Join("\n, ", _conversationInfoSerializableList));
        }

        private async Task AddUrlToMonitor(IDialogContext context, string text)
        {
            var url = text.Trim().Split(' ')[1];
            if (string.IsNullOrEmpty(url) && !_monitoringDictionary.ContainsKey(url))
            {
                await context.PostAsync("AddUrlToMonitor -> ERROR");
            }
            else
            {
                _monitoringDictionary.Add(url, string.Empty);
                await ShowUrlList(context);
            }
        }

        private async Task ShowUrlList(IDialogContext context)
        {
            await context.PostAsync(String.Join(", \n", _monitoringDictionary.Select(x => x.Key)));
        }

        private async Task RemoveUrl(IDialogContext context, string text)
        {
            var url = text.Trim().Split(' ')[1];
            if (string.IsNullOrEmpty(url) && !_monitoringDictionary.ContainsKey(url))
            {
                await context.PostAsync("RemoveUrl -> ERROR");
            }
            else
            {
                _monitoringDictionary.Remove(url);
                await ShowUrlList(context);
            }
        }
        #endregion

        private void AddNewConversationInfo(ConversationInfo info)
        {
            var serializableInfo = JsonConvert.SerializeObject(info);
            if (!_conversationInfoSerializableList.Contains(serializableInfo))
            {
                _conversationInfoSerializableList.Add(serializableInfo);
            }
        }

        #region BackgroundJob
        private async Task RepeatBackgroundJob()
        {
            await Task.Delay((int)(_delayInMinutes * 60 * 1000));
            BackgroundJob();
            RepeatBackgroundJob();
        }

        private async Task ResetDelay(string text, IDialogContext context)
        {
            float newDelayInMinutes = default(float);
            float.TryParse(text.Trim().Split(' ')[1], out newDelayInMinutes);

            if (newDelayInMinutes == default(float))
            {
                await context.PostAsync("ResetDelay -> ERROR");
            }
            else
            {
                _delayInMinutes = newDelayInMinutes;
                await ShowSettings(context);
            }
        }

        private async Task BackgroundJob()
        {
            foreach (var site in _monitoringDictionary)
            {
                var data = await GetSiteData(site.Key);
                if (string.IsNullOrEmpty(data))
                {
                    // ERROR NOTIFICATIOn
                }
                else
                {
                    if (!string.IsNullOrEmpty(site.Value) && site.Value != data)
                    {
                        notification();
                        _monitoringDictionary.Remove(site.Key);
                        _monitoringDictionary.Add(site.Key, data);
                    }
                    if (string.IsNullOrEmpty(site.Value))
                    {
                        _monitoringDictionary.Remove(site.Key);
                        _monitoringDictionary.Add(site.Key, data);
                    }
                }
            }
        }
        #endregion

        #region http
        private async Task<string> GetSiteData(string url)
        {
            HttpResponseMessage response = await _client.GetAsync(url);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : string.Empty;
        }
        #endregion

        [Serializable]
        private class ConversationInfo
        {
            public string toId { get; set; }
            public string toName { get; set; }
            public string fromId { get; set; }
            public string fromName { get; set; }
            public string serviceUrl { get; set; }
            public string channelId { get; set; }
            public string conversationId { get; set; }
        }
    }
}