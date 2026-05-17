namespace EasyStock.Domain.Enums.Notifications;

public enum CanalNotificacao
{
    Email = 1,
    Sms = 2,
    WhatsApp = 3,
    InApp = 4,
    /// <summary>Web Push (PWA via Service Worker + VAPID). Onda 2.2.</summary>
    Push = 5
}
