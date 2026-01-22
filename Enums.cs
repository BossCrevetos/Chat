namespace Backend_chat
{
    public enum UserStatus
    {
        Offline = 0,
        Online = 1,
        Away = 2,
        Busy = 3,
        DoNotDisturb = 4
    }

    public enum MessageType
    {
        Text = 0,
        Image = 1,
        File = 2,
        Video = 3,
        Audio = 4
    }
}