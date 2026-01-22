namespace Backend_chat  // ИЗМЕНИЛ ЗДЕСЬ - убрал ".Enums"
{
    public enum UserStatus
    {
        Offline,
        Online,
        DoNotDisturb,
        Away
    }

    public enum MessageType
    {
        Text,
        Image,
        File,
        System
    }
}