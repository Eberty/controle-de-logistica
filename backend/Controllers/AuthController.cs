using AssetManagement.Data;
using AssetManagement.Dtos;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] AuthLoginRequest request)
    {
        var username = NormalizeUsername(request.Username);
        var user = await FindUserByUsernameAsync(username);

        if (!PasswordHasher.VerifyOrFail(request.Password, user?.PasswordHash) || user is null)
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

    private const int MinPasswordLength = 8;

    [HttpPost("initial-admin")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> CreateInitialAdmin([FromBody] InitialAdminRequest request)
    {
        if (await Context.Users.AnyAsync(x => x.Role == "Admin"))
            return Conflict(new { message = "O administrador inicial já foi criado." });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Informe a senha do administrador." });

        if (request.Password.Length < MinPasswordLength)
            return BadRequest(new { message = $"A senha deve ter pelo menos {MinPasswordLength} caracteres." });

        var admin = new User
        {
            Username = "admin",
            PasswordHash = PasswordHasher.Hash(request.Password),
            FullName = "Administrador do Sistema",
            Role = "Admin",
            MilitaryId = "Admin"
        };

        return await PersistNewUserAsync(
            admin,
            admin,
            "Administrador inicial criado.",
            "O administrador inicial já foi criado.",
            () => Context.Users.AnyAsync(x => x.Role == "Admin"));
    }

    [HttpPost("register")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Register([FromBody] AuthRegisterRequest request)
    {
        var username = NormalizeUsername(request.Username);
        var fullName = request.FullName.Trim();
        var militaryId = request.MilitaryId.Trim();

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(request.Password)
            || string.IsNullOrWhiteSpace(fullName)
            || string.IsNullOrWhiteSpace(militaryId)
            || string.IsNullOrWhiteSpace(request.AdminUsername)
            || string.IsNullOrWhiteSpace(request.AdminPassword))
            return BadRequest(new { message = "Preencha todos os campos obrigatórios." });

        if (request.Password.Length < MinPasswordLength)
            return BadRequest(new { message = $"A senha deve ter pelo menos {MinPasswordLength} caracteres." });

        var adminUsername = NormalizeUsername(request.AdminUsername);
        var admin = await FindUserByUsernameAsync(adminUsername);

        if (admin is null
            || !IsAdmin(admin)
            || !PasswordHasher.Verify(request.AdminPassword, admin.PasswordHash))
            return Unauthorized(new { message = "Credenciais de administrador inválidas." });

        if (await UsernameExistsAsync(username))
            return Conflict(new { message = "Esse usuário já existe." });

        var user = new User
        {
            Username = username,
            PasswordHash = PasswordHasher.Hash(request.Password),
            FullName = fullName,
            Role = request.IsAdmin ? "Admin" : "User",
            MilitaryId = militaryId
        };

        return await PersistNewUserAsync(
            user,
            admin,
            request.IsAdmin ? "Administrador cadastrado." : "Usuário cadastrado.",
            "Esse usuário já existe.",
            () => UsernameExistsAsync(username));
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var user = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == currentUser.Id);
        if (user is null)
            return Unauthorized(new { message = "Sessão inválida." });

        return Ok(new UserResponse(user.Id, user.Username, user.FullName, user.Role, user.MilitaryId));
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var query = Context.Users.AsNoTracking();

        if (!IsAdmin(currentUser))
            query = query.Where(x => x.Id == currentUser.Id);

        var users = await query
            .OrderByDescending(x => x.Id == currentUser.Id)
            .ThenBy(x => x.FullName)
            .ThenBy(x => x.Username)
            .Select(x => new UserResponse(x.Id, x.Username, x.FullName, x.Role, x.MilitaryId))
            .ToListAsync();

        return Ok(users);
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var (ok, currentUser, error) = await TryGetAdminUserAsync();
        if (!ok) return error!;

        if (id == currentUser.Id)
            return BadRequest(new { message = "Você não pode excluir o próprio usuário." });

        var user = await Context.Users.FirstOrDefaultAsync(x => x.Id == id);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "O administrador principal não pode ser excluído." });

        await using var transaction = await Context.Database.BeginTransactionAsync();
        var userNotes = await Context.UserNotes.Where(x => x.UserId == id).ToListAsync();
        var removedMuralNotes = userNotes.Where(x => x.IsPublic).ToList();
        Context.UserNotes.RemoveRange(userNotes);
        Context.Users.Remove(user);
        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Delete,
            AuditEntityTypes.User,
            id.ToString(),
            user.Username,
            "Usuário removido.");
        if (removedMuralNotes.Count > 0)
            await _auditLogger.LogAsync(
                currentUser,
                AuditActions.Remove,
                AuditEntityTypes.Mural,
                id.ToString(),
                user.Username,
                $"{removedMuralNotes.Count} anotação(ões) removida(s) do mural com a exclusão do usuário: {string.Join(" | ", removedMuralNotes.Select(x => x.Title))}.");
        await transaction.CommitAsync();
        SessionStore.RemoveSessionsByUserId(id);

        return NoContent();
    }

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserUpdateRequest request)
    {
        var (ok, currentUser, error) = await TryGetCurrentUserAsync();
        if (!ok) return error!;

        var currentUserIsAdmin = IsAdmin(currentUser);

        if (!currentUserIsAdmin && id != currentUser.Id)
            return StatusCode(403, new { message = "Sem permissão para alterar outro usuário." });

        var username = NormalizeUsername(request.Username);
        var fullName = request.FullName.Trim();
        var militaryId = request.MilitaryId.Trim();

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(fullName)
            || string.IsNullOrWhiteSpace(militaryId))
            return BadRequest(new { message = "Preencha todos os campos obrigatórios." });

        var user = await Context.Users.FirstOrDefaultAsync(x => x.Id == id);

        if (user is null)
            return NotFound(new { message = "Usuário não encontrado." });

        if (await UsernameExistsAsync(username, id))
            return Conflict(new { message = "Esse usuário já existe." });

        var usernameChanged = !string.Equals(user.Username, username, StringComparison.Ordinal);
        var currentPasswordValid = id == currentUser.Id
            && !string.IsNullOrWhiteSpace(request.CurrentPassword)
            && PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash);

        if (id == currentUser.Id && usernameChanged && !currentPasswordValid)
            return Unauthorized(new { message = "Senha atual inválida." });

        if (!currentUserIsAdmin && request.IsAdmin)
            return BadRequest(new { message = "Usuário comum não pode definir administrador." });

        var userIsAdmin = IsAdmin(user);
        var promotesToAdmin = currentUserIsAdmin && !userIsAdmin && request.IsAdmin;

        if (promotesToAdmin
            && (string.IsNullOrWhiteSpace(request.AdminPassword)
                || !PasswordHasher.Verify(request.AdminPassword, currentUser.PasswordHash)))
            return Unauthorized(new { message = "Senha do administrador inválida." });

        var adminChangingOtherUserPassword =
            currentUserIsAdmin && id != currentUser.Id && !string.IsNullOrWhiteSpace(request.Password);

        if (adminChangingOtherUserPassword && string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "A senha do administrador principal só pode ser alterada por ele mesmo." });

        if (adminChangingOtherUserPassword
            && (string.IsNullOrWhiteSpace(request.AdminPassword)
                || !PasswordHasher.Verify(request.AdminPassword, currentUser.PasswordHash)))
            return Unauthorized(new { message = "Senha do administrador inválida." });

        if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(username, "admin", StringComparison.Ordinal))
            return BadRequest(new { message = "O nome de usuário do administrador principal não pode ser alterado." });

        if (IsAdmin(user) && !request.IsAdmin)
        {
            if (string.Equals(user.Username, "admin", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "O administrador principal não pode ser rebaixado." });

            var otherAdminsExist = await Context.Users.AnyAsync(x => x.Id != id && x.Role == "Admin");
            if (!otherAdminsExist)
                return BadRequest(new { message = "É necessário manter pelo menos um administrador." });
        }

        var nextRole = currentUserIsAdmin ? (request.IsAdmin ? "Admin" : "User") : user.Role;
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
        {
            changedFields.Add("Senha: alterada");
            if (request.Password.Length < MinPasswordLength)
                return BadRequest(new { message = $"A senha deve ter pelo menos {MinPasswordLength} caracteres." });

            if (id == currentUser.Id && !currentPasswordValid)
                return Unauthorized(new { message = "Senha atual inválida." });
        }

        await using var transaction = await Context.Database.BeginTransactionAsync();
        user.Username = username;
        user.FullName = fullName;
        user.MilitaryId = militaryId;
        user.Role = nextRole;

        if (!string.IsNullOrWhiteSpace(request.Password))
            user.PasswordHash = PasswordHasher.Hash(request.Password);

        await Context.SaveChangesAsync();
        await _auditLogger.LogAsync(
            currentUser,
            AuditActions.Update,
            AuditEntityTypes.User,
            id.ToString(),
            user.Username,
            changedFields.Count > 0
                ? $"Campos editados: {string.Join(" | ", changedFields)}."
                : "Usuário editado.");
        await transaction.CommitAsync();

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            TryGetBearerToken(out var currentToken);
            SessionStore.RemoveSessionsByUserId(user.Id, id == currentUser.Id ? currentToken : null);
        }

        return Ok(new UserResponse(user.Id, user.Username, user.FullName, user.Role, user.MilitaryId));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (TryGetBearerToken(out var token))
            SessionStore.RemoveSession(token);

        return NoContent();
    }

    private async Task<IActionResult> PersistNewUserAsync(
        User user,
        User actor,
        string auditDetails,
        string conflictMessage,
        Func<Task<bool>> conflictAlreadyExists)
    {
        await using var transaction = await Context.Database.BeginTransactionAsync();
        try
        {
            Context.Users.Add(user);
            await Context.SaveChangesAsync();
            await _auditLogger.LogAsync(
                actor,
                AuditActions.Create,
                AuditEntityTypes.User,
                user.Id.ToString(),
                user.Username,
                auditDetails);
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            if (await conflictAlreadyExists())
                return Conflict(new { message = conflictMessage });
            throw;
        }

        return Created(
            $"/api/auth/users/{user.Id}",
            new UserResponse(user.Id, user.Username, user.FullName, user.Role, user.MilitaryId));
    }

    private Task<User?> FindUserByUsernameAsync(string username) =>
        QueryUsersByUsername(username).FirstOrDefaultAsync();

    private Task<bool> UsernameExistsAsync(string username, int? ignoredUserId = null) =>
        QueryUsersByUsername(username)
            .AnyAsync(x => !ignoredUserId.HasValue || x.Id != ignoredUserId.Value);

    private static string NormalizeUsername(string value) => value.Trim();
}
