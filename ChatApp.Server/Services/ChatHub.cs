using Microsoft.AspNetCore.SignalR;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChatApp.Server.Services;

public class ChatHub : Hub
{
    private static readonly Dictionary<string, string> Users = new();
    private static readonly Dictionary<string, int> Rooms = new() { { "General", 1 } };
    private static readonly Dictionary<string, HashSet<string>> RoomUsers = new() { { "General", new HashSet<string>() } };

    public async Task SendMessage(string user, string room, string message)
    {
        if (string.IsNullOrWhiteSpace(message) || !RoomUsers.ContainsKey(room)) return;

        await Clients.Group(room).SendAsync("ReceiveMessage", user, message);
    }

    public async Task LoginUser(string user)
    {
        if (string.IsNullOrWhiteSpace(user) || Users.ContainsKey(user))
        {
            await Clients.Caller.SendAsync("LoginFailed", "Invalid or duplicate username.");
            return;
        }

        Users[user] = Context.ConnectionId;
        await Clients.Caller.SendAsync("LoginSuccess", user);
        await Clients.All.SendAsync("UserLoggedIn", user);
    }

    public async Task RequestChatRooms()
    {
        await Clients.Caller.SendAsync("ReceiveChatRooms", Rooms.Keys.ToList(), GetRoomUserCounts());
    }

    public async Task JoinRoom(string user, string roomName)
    {
        if (!Users.ContainsKey(user)) return;

        if (!RoomUsers.ContainsKey(roomName))
            RoomUsers[roomName] = new HashSet<string>();

        RoomUsers[roomName].Add(user);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.OthersInGroup(roomName).SendAsync("ReceiveSystemMessage", $"{user} joined the chat room.");
        await Clients.Group(roomName).SendAsync("ReceiveUsersInRoom", RoomUsers[roomName].ToList());
        await Clients.All.SendAsync("ReceiveChatRooms", Rooms.Keys.ToList(), GetRoomUserCounts());
        await Clients.Caller.SendAsync("RoomJoined", roomName);
    }

    public async Task LeaveRoom(string user, string roomName)
    {
        if (RoomUsers.ContainsKey(roomName) && RoomUsers[roomName].Contains(user))
        {
            RoomUsers[roomName].Remove(user);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
            await Clients.Group(roomName).SendAsync("ReceiveSystemMessage", $"{user} left the chat room.");
            await Clients.Group(roomName).SendAsync("ReceiveUsersInRoom", RoomUsers[roomName].ToList());
            await Clients.All.SendAsync("ReceiveChatRooms", Rooms.Keys.ToList(), GetRoomUserCounts());
            await SendRoomUpdates();
        }
    }

    public async Task CreateRoom(string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName) || roomName.Length > 40 || Rooms.ContainsKey(roomName)) return;

        Rooms[roomName] = 0;
        RoomUsers[roomName] = new HashSet<string>();

        await Clients.All.SendAsync("ReceiveChatRooms", Rooms.Keys.ToList(), GetRoomUserCounts());
    }

    public async Task DeleteRoom(string roomName)
    {
        if (roomName == "General" || !Rooms.ContainsKey(roomName) || RoomUsers[roomName].Count > 0) return;

        Rooms.Remove(roomName);
        RoomUsers.Remove(roomName);

        await Clients.All.SendAsync("ReceiveChatRooms", Rooms.Keys.ToList(), GetRoomUserCounts());
    }

    public async Task RequestUsersInRoom(string roomName)
    {
        if (RoomUsers.ContainsKey(roomName))
            await Clients.Caller.SendAsync("ReceiveUsersInRoom", RoomUsers[roomName].ToList());
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var user = Users.FirstOrDefault(u => u.Value == Context.ConnectionId).Key;
        if (!string.IsNullOrEmpty(user))
        {
            Users.Remove(user);

            foreach (var room in RoomUsers.Keys.ToList())
            {
                if (RoomUsers[room].Contains(user))
                {
                    RoomUsers[room].Remove(user);
                    await Clients.Group(room).SendAsync("ReceiveSystemMessage", $"{user} left the chat room.");
                }
            }

            await SendRoomUpdates();
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task SendRoomUpdates()
    {
        await Clients.All.SendAsync("ReceiveChatRooms", Rooms.Keys.ToList(), GetRoomUserCounts());
    }

    private Dictionary<string, int> GetRoomUserCounts()
    {
        return RoomUsers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
    }
}