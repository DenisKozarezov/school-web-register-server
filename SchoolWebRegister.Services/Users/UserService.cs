﻿using System.Data;
using System.Data.Entity.Core;
using System.Security.Claims;
using SchoolWebRegister.DAL.Repositories;
using SchoolWebRegister.Domain;
using SchoolWebRegister.Domain.Entity;
using SchoolWebRegister.Domain.Helpers;
using SchoolWebRegister.Domain.Permissions;

namespace SchoolWebRegister.Services.Users
{
    public sealed class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordValidator _passwordValidator;
        public UserService(IUserRepository userRepository, IPasswordValidator passwordValidator)
        {
            _userRepository = userRepository;
            _passwordValidator = passwordValidator;
        }

        private async Task<ApplicationUser?> ValidateIfUserExist(string guid)
        {
            if (string.IsNullOrEmpty(guid)) throw new ArgumentNullException();

            var existingEntity = await _userRepository.GetByIdAsync(guid);
            if (existingEntity != null)
                throw new DuplicateNameException("Пользователь с таким Id уже существует.");
            return existingEntity;
        }
        private async Task<ApplicationUser?> ValidateIfUserNotExist(string guid)
        {
            if (string.IsNullOrEmpty(guid)) throw new ArgumentNullException();

            var existingEntity = await _userRepository.GetByIdAsync(guid);
            if (existingEntity == null)
                throw new ObjectNotFoundException("Пользователя с таким Id не существует.");
            return existingEntity;
        }
        public async Task<bool> IsUserInRole(ApplicationUser user, UserRole role)
        {
            return await _userRepository.IsUserInRole(user, role);
        }
        public async Task<bool> UserHasClaim(ApplicationUser user, string key, string value)
        {
            try
            {
                var claims = await _userRepository.GetClaims(user);
                return claims.Any(x => x.ClaimType.Equals(key) && x.ClaimValue.Equals(value));
            }
            catch
            {
                return false;
            }
        }
        public bool ValidatePassword(ApplicationUser user, string password)
        {
            if (string.IsNullOrEmpty(password))
                return false;

            return HashPasswordHelper.VerifyPassword(user, password);
        }
        public async Task<BaseResponse> CreateUser(ApplicationUser user)
        {
            try
            {
                await ValidateIfUserExist(user.Id);
                var result = await _userRepository.AddAsync(user);

                return new BaseResponse(
                    code: result != null ? StatusCode.Success : StatusCode.Error
                );
            }
            catch (Exception ex)
            {
                return new BaseResponse(
                    code: StatusCode.Error,
                    error: new ErrorType { Message = ex.Message }
                );
            }
        }
        public async Task<BaseResponse> DeleteUser(string guid)
        {
            try
            {
                ApplicationUser? user = await ValidateIfUserNotExist(guid);
                await _userRepository.DeleteAsync(user);

                return new BaseResponse(
                    code: StatusCode.Success
                );
            }
            catch (Exception ex)
            {
                return new BaseResponse(
                    code: StatusCode.Error,
                    error: new ErrorType { Message = ex.Message }
                );
            }
        }
        public async Task<ApplicationUser?> GetUserById(string guid)
        {
            try
            {
                return await ValidateIfUserNotExist(guid);
            }
            catch
            {
                return null;
            }
        }
        public async Task<ApplicationUser?> GetUserByLogin(string login)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(login)) return null;
               
                return await _userRepository.GetUserByLoginAsync(login);
            }
            catch
            {
                return null;
            }
        }
        public async Task<BaseResponse> UpdateUser(ApplicationUser modifiedUser)
        {
            try
            {
                ApplicationUser? editUser = await ValidateIfUserNotExist(modifiedUser.Id);

                var result = await _userRepository.UpdateAsync(editUser);  
                return new BaseResponse(
                    code: result != null ? StatusCode.Success : StatusCode.Error,
                    data: "Пароль успешно сменен."
                );
            }
            catch (Exception ex)
            {
                return new BaseResponse(
                    code: StatusCode.Error,
                    error: new ErrorType { Message = ex.Message }
                );
            }
        }
        public IQueryable<ApplicationUser> GetUsers()
        {
            try
            {
                return _userRepository.Select();
            }
            catch
            {
                return Enumerable.Empty<ApplicationUser>().AsQueryable();
            }
        }
        public async Task<IList<string>> GetUserRoles(ApplicationUser user)
        {
            try
            {
                if (user == null) throw new ArgumentNullException();
                return await _userRepository.GetUserRoles(user);
            }
            catch
            {
                return new List<string>();
            }
        }
        public async Task GrantPermission(ApplicationUser user, Permissions permissions)
        {
            if (user == null) return;

            IEnumerable<Permissions> allFlags = Enum.GetValues(typeof(Permissions))
                            .Cast<Enum>()
                            .Where(m => permissions.HasFlag(m))
                            .Cast<Permissions>();

            await _userRepository.AddClaimsAsync(user, allFlags.Select(flag => new Claim("permission", flag.ToString())));
        }
        public async Task RemovePermission(ApplicationUser user, params Permissions[] permissions)
        {
            if (user == null) return;

            foreach (Permissions permission in permissions)
            {
                await _userRepository.RemoveClaimAsync(user, new Claim("permission", permission.ToString()));
            }
        }
        public async Task AddClaims(ApplicationUser user, IEnumerable<Claim> claims)
        {
            await _userRepository.AddClaimsAsync(user, claims);
        }
        public async Task AddToRoles(ApplicationUser user, IEnumerable<UserRole> roles)
        {
            await _userRepository.AddRolesAsync(user, roles.Select(x => x.ToString()));

            var claims = roles.Select(x => new Claim(ClaimTypes.Role, x.ToString()));
            await AddClaims(user, claims);
        }
        public async Task<BaseResponse> ChangePassword(ApplicationUser user, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword))
                return new BaseResponse(
                    code: StatusCode.Error,
                    error: new ErrorType
                    {
                        Message = "Требуется новый пароль.",
                        Type = new string[] { "AUTH_NOT_AUTHORIZED", "MISSING_DATA" }
                    }
                );

            try
            {
                bool isValid = _passwordValidator.IsValid(newPassword);
                if (!isValid)
                {
                    return new BaseResponse(
                        code: StatusCode.Error,
                        error: new ErrorType
                        {
                            Message = "Новый пароль не соответствует требованиям безопасности.",
                            Type = new string[] { "AUTH_NOT_AUTHORIZED", "INVALID_DATA" }
                        }
                    );
                }

                bool isSame = HashPasswordHelper.VerifyPassword(user, newPassword);
                if (!isSame)
                {
                    string hash = HashPasswordHelper.HashPassword(newPassword);
                    user.PasswordHash = hash;
                    return await UpdateUser(user);
                }

                return new BaseResponse(
                    code: StatusCode.Error,
                    error: new ErrorType
                    {
                       Message = "Новый пароль не должен совпадать со старым.",
                       Type = new string[] { "AUTH_NOT_AUTHORIZED", "INVALID_DATA" }
                    }
                );
            }
            catch
            {
                return new BaseResponse(
                    code: StatusCode.Error
                );
            }
        }
        public async Task<ApplicationUser?> ValidateCredentials(string? login, string? password)
        {
            try
            {
                ApplicationUser? user = await GetUserByLogin(login);

                if (user == null || string.IsNullOrEmpty(user.PasswordHash)) return null;

                bool isValid = ValidatePassword(user, password);
                return isValid ? user : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
