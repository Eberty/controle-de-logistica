using AssetManagement.DTOs;
using AssetManagement.Data;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AssetManagement.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : AuthenticatedControllerBase
{
    private readonly IAuditLogger _auditLogger;

    public AuthController(AppDbContext context, IAuthSessionStore sessionStore, IAuditLogger auditLogger)
        : base(context, sessionStore)
    {
        _auditLogger = auditLogger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthLoginRequest request)
    {
        var user = await Context.Users.FirstOrDefaultAsync(x => x.Username == request.Username);

        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Usuário ou senha inválidos." });

        if (PasswordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = PasswordHasher.Hash(request.Password);
            await Context.SaveChangesAsync();
        }

        var token = SessionStore.CreateSession(user);

        return Ok(new AuthLoginResponse(
            token,
            new UserResponse(user.Id, user.Username, user.FullName, user.Role, user.MilitaryId)));
    }

    [HttpGet("setup")]
    public async Task<IActionResult> Setup()
    {
        var hasAdmin = await Context.Users.AnyAsync(x => x.Role == "Admin");
        return Ok(new { requiresInitialAdmin = !hasAdmin });
    }

    [HttpPost("initial-admin")]
    public async Task<IActionResult> CreateInitialAdmin([FromBody] InitialAdminRequest request)
    {
        if (await Context.Users.AnyAsync(x => x.Role == "Admin"))
            return Conflict(new { message = "O administrador inicial já foi criado." });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Informe a senha do administrador." });

        var admin = new AssetManagement.Models.User
        {
            Username = "admin",
            PasswordHash = PasswordHasher.Hash(request.Password),
            FullName = "Administrador do Sistema",
            Role = "Admin",
            MilitaryId = "Admin"
        };

        Context.Users.Add(admin);
        await Context.SaveChangesAsync();

        return Created(
            $"/api/auth/users/{admin.Id}",
            new UserResponse(admin.Id, admin.Username, admin.FullName, admin.Role, admin.MilitaryId));
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] AuthRegisterRequest request)
    {
        var username = request.Username.Trim();
        var fullName = request.FullName.Trim();
        var militaryId = request.MilitaryId.Trim();

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(request.Password)
            || string.IsNullOrWhiteSpace(fullName)
            || string.IsNullOrWhiteSpace(militaryId)
            || string.IsNullOrWhiteSpace(request.AdminUsername)
            || string.IsNullOrWhiteSpace(request.AdminPassword))
            return BadRequest(new { message = "Preencha todos os campos obrigatórios." });

        if (await Context.Users.AnyAsync(x => x.Username == username))
            return Conflict(new { message = "Esse usuário já existe." });

        var adminUsername = request.AdminUsername.Trim();
        var admin = await Context.Users.FirstOrDefaultAsync(x => x.Username == adminUsername);

        if (admin is null
            || !string.Equals(admin.Role, "Admin", StringComparison.OrdinalIgnoreCase)
            || !PasswordHasher.Verify(request.AdminPassword, admin.PasswordHash))
            return Unauthorized(new { message = "Credenciais de administrador inválidas." });

        var user = new AssetManagement.Models.User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(request.Password),
            FullName = fullName,
            Role = request.IsAdmin ? "Admin" : "User",
            MilitaryId = militaryId
        };

        Context.Users.Add(user);
        await Context.SaveChangesAsync();

        await _auditLogger.LogAsync(
            admin,
            "Criação",
            "Usuário",
            user.Id.ToString(),
            user.Username,
            request.IsAdmin ? "Administrador cadastrado." : "Usuário cadastrado.");

        return Created(
            $"/api/auth/users/{user.Id}",
            new UserResponse(user.Id, user.Username, user.FullName, user.Role, user.MilitaryId));
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!TryGetCurrentUser(out var currentUser, out var error))
            return error!;

        var user = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == currentUser.Id);
        return Ok(new UserResponse(user.Id, user.Username, user.FullName, user.Role, user.MilitaryId));
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        if (!TryGetCurrentUser(out var currentUser, out var error))
            return error!;

        var query = Context.Users.AsNoTracking();

        if (!string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.Id == currentUser.Id);

        var users = await query
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Username)
            .Select(x => new UserResponse(x.Id, x.Username, x.FullName, x.Role, x.MilitaryId))
            .ToListAsync();

        return Ok(users);
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (!TryGetAdminUser(out var currentUser, out var error))
            return error!;

        if (id == currentUser.Id)
            return BadRequest(new { message = "Você não pode excluir o próprio usuário." });

        var user = await Context.Users.FirstOrDefaultAsync(x => x.Id == id);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        Context.UserNotes.RemoveRange(Context.UserNotes.Where(x => x.UserId == id));
        Context.Users.Remove(user);
        await Context.SaveChangesAsync();

        await _auditLogger.LogAsync(
            currentUser,
            "Exclusão",
            "Usuário",
            id.ToString(),
            user.Username,
            "Usuário removido.");

        return NoContent();
    }

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserUpdateRequest request)
    {
        if (!TryGetCurrentUser(out var currentUser, out var error))
            return error!;

        var currentUserIsAdmin = string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase);

        if (!currentUserIsAdmin && id != currentUser.Id)
            return Forbid();

        var username = request.Username.Trim();
        var fullName = request.FullName.Trim();
        var militaryId = request.MilitaryId.Trim();

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(fullName)
            || string.IsNullOrWhiteSpace(militaryId))
            return BadRequest(new { message = "Preencha todos os campos obrigatórios." });

        var user = await Context.Users.FirstOrDefaultAsync(x => x.Id == id);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        if (await Context.Users.AnyAsync(x => x.Id != id && x.Username == username))
            return Conflict(new { message = "Esse usuário já existe." });

        var usernameChanged = !string.Equals(user.Username, username, StringComparison.Ordinal);

        if (id == currentUser.Id
            && usernameChanged
            && (string.IsNullOrWhiteSpace(request.CurrentPassword)
                || !PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash)))
            return Unauthorized(new { message = "Senha atual inválida." });

        if (!currentUserIsAdmin && request.IsAdmin)
            return BadRequest(new { message = "Usuário comum não pode definir administrador." });

        var userIsAdmin = string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase);
        var promotesToAdmin = currentUserIsAdmin && !userIsAdmin && request.IsAdmin;

        if (promotesToAdmin
            && (string.IsNullOrWhiteSpace(request.AdminPassword)
                || !PasswordHasher.Verify(request.AdminPassword, currentUser.PasswordHash)))
            return Unauthorized(new { message = "Senha do administrador inválida." });

        var adminChangingOtherUserPassword =
            currentUserIsAdmin && id != currentUser.Id && !string.IsNullOrWhiteSpace(request.Password);

        if (adminChangingOtherUserPassword
            && (string.IsNullOrWhiteSpace(request.AdminPassword)
                || !PasswordHasher.Verify(request.AdminPassword, currentUser.PasswordHash)))
            return Unauthorized(new { message = "Senha do administrador inválida." });

        if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase) && !request.IsAdmin)
            return BadRequest(new { message = "Não é possível remover o perfil de administrador." });

        var nextRole = currentUserIsAdmin && request.IsAdmin ? "Admin" : user.Role;
        var changedFields = new List<string>();

        if (!string.Equals(user.Username, username, StringComparison.Ordinal))
            changedFields.Add($"Usuário: {FormatAuditValue(user.Username)} -> {FormatAuditValue(username)}");

        if (!string.Equals(user.FullName, fullName, StringComparison.Ordinal))
            changedFields.Add($"Nome: {FormatAuditValue(user.FullName)} -> {FormatAuditValue(fullName)}");

        if (!string.Equals(user.MilitaryId, militaryId, StringComparison.Ordinal))
            changedFields.Add($"Identificação: {FormatAuditValue(user.MilitaryId)} -> {FormatAuditValue(militaryId)}");

        if (!string.Equals(user.Role, nextRole, StringComparison.Ordinal))
            changedFields.Add($"Perfil: {FormatAuditValue(user.Role)} -> {FormatAuditValue(nextRole)}");

        if (!string.IsNullOrWhiteSpace(request.Password))
            changedFields.Add("Senha: alterada");

        user.Username = username;
        user.FullName = fullName;
        user.MilitaryId = militaryId;
        user.Role = nextRole;

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (id == currentUser.Id
                && (string.IsNullOrWhiteSpace(request.CurrentPassword)
                    || !PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash)))
                return Unauthorized(new { message = "Senha atual inválida." });

            user.PasswordHash = PasswordHasher.Hash(request.Password);
        }

        await Context.SaveChangesAsync();

        await _auditLogger.LogAsync(
            currentUser,
            "Atualização",
            "Usuário",
            id.ToString(),
            user.Username,
            changedFields.Count > 0
                ? $"Campos editados: {string.Join(" | ", changedFields)}."
                : "Usuário editado.");

        return Ok(new UserResponse(user.Id, user.Username, user.FullName, user.Role, user.MilitaryId));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var header = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";

        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            SessionStore.RemoveSession(header[prefix.Length..].Trim());

        return NoContent();
    }

    private static string FormatAuditValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "vazio" : value.Trim();
}
