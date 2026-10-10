namespace Prowl.Launcher;

sealed record NewsPost(string File, string Title, DateOnly Date, string Summary = "", string Author = "", string Thumbnail = "");
