
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.SignalR;
namespace WebApplication1.Hubs
{
    public class ChatHub:Hub
    {
        private readonly IUsersManager usersManager;
        private readonly IConversationsManager conversationsManager;
        private readonly IMediaManager mediaManager;
        public readonly IOfflineMessageManager _offlineMessageManager;
        public ChatHub(IOfflineMessageManager offlineMessageManagerRepository,IUsersManager usersManager, IConversationsManager conversationsManager, IMediaManager mediaManager)
        {
            this.usersManager = usersManager;
            this.conversationsManager = conversationsManager;
            this.mediaManager = mediaManager;
            _offlineMessageManager = offlineMessageManagerRepository;
        }

        public async Task<string> SendMessage(string userId, string message)
        {
            await Clients.Others.SendAsync("ReceiveMessage", userId, message);
            return message;
        }

        public async Task<string> SendPrivateMessage(string userEmail, string message = null, byte[] MediaData = null)
        {

            var senderUser = usersManager.GetUserByConnectionId(Context.ConnectionId);
            var friend = usersManager.GetUserByEmail(userEmail);
            var friendConnections = friend.Connections.Where(x => x.IsConnected);

            bool isUserOffline = !friendConnections.Any();
            if (!isUserOffline)
            {
                foreach (var connection in friendConnections)
                {
                    await Clients.Client(connection.ConnectionID).SendAsync("ReceivePrivateMessage", userEmail, message, MediaData);
                }
            }
            if (isUserOffline && MediaData != null && message != null)
            {
                // Save the media with the message in the database for later retrieval
                mediaManager.StoreMediaAndGetUrlAsync( MediaData);
                // save the mediaurl withmessage in database
                //then get the data by offline data as seperated data
                string mediaUrl = await mediaManager.StoreMediaAndGetUrlAsync(MediaData);

                // Save the media URL with the message in the database
                // This is a pseudo-code function, replace with your actual database call
                // should adding await
                _offlineMessageManager.StoreOfflineMessage(senderUser.ID, friend.ID, mediaUrl);

            }

            // Inser in to database..
            var conversationModel = conversationsManager.GetConversationByUsersId(senderUser.ID, friend.ID);

                if (conversationModel == null)
                {
                    var conversationId = conversationsManager.AddOrUpdateConversation(senderUser.ID, friend.ID);
                    conversationsManager.AddReply(MediaData, message, conversationId, senderUser.ID);
                }
                else
                {
                    conversationsManager.AddReply(MediaData, message, conversationModel.ID, senderUser.ID);

                }
            if (!isUserOffline)
            {
                List<string> mediaUrls = _offlineMessageManager.GetOfflineMediaUrlsForUser(senderUser.ID);
                foreach (string mediaUrl in mediaUrls)
                {
                    // Get media content from blob storage
                    byte[] mediaContent = await mediaManager.GetMediaFromBlobStorage(mediaUrl);

                    foreach (var connection in friendConnections)
                    {
                        await Clients.Client(connection.ConnectionID).SendAsync("ReceivePrivateMessage", userEmail, message, mediaContent);
                    }

                    // Delete the offline messages related to this media
                    _offlineMessageManager.DeleteOfflineMessagesForUser(senderUser.ID);
                }
            }
            return message;
        }

       

       



            public async Task OnConnect(string userEmail)
        {
            var user = usersManager.GetUserByEmail(userEmail);
            usersManager.AddUserConnections(new ConnectionModel
            {
                ConnectionID = Context.ConnectionId,
                IsConnected = true,
                UserAgent = Context.GetHttpContext().Request.Headers["User-Agent"],
                UserID = user.ID
            });

            await base.OnConnectedAsync();
        }

        public async Task OnDisconnect(string userEmail)
        {
            var user = usersManager.GetUserByEmail(userEmail);
            usersManager.UpdateUserConnectionsStatus(user.ID, false, Context.ConnectionId);
            await base.OnDisconnectedAsync(null);
        }
        public async Task SendMedia(byte[] mediaData)
        {
            // Broadcast the received media data to all connected clients
            await Clients.All.SendAsync("ReceiveMedia", mediaData);
        }

    }
}

