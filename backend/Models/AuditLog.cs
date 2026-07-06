namespace AssetManagement.Models;

public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int ActorUserId { get; set; }
    public string ActorUserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public static class AuditEntityTypes
{
    public const string Item = "Item";
    public const string Movement = "Movimentação";
    public const string Location = "Localização";
    public const string User = "Usuário";
    public const string Calendar = "Calendário";
    public const string Mural = "Mural";
}

public static class AuditActions
{
    public const string Create = "Criação";
    public const string Update = "Atualização";
    public const string Delete = "Exclusão";
    public const string Transfer = "Transferência";
    public const string Publish = "Publicação";
    public const string Remove = "Remoção";
}
