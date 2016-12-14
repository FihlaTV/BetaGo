﻿using BetaGo.Server.DataModels.Auth;
using BetaGo.Server.Services.Database;
using BetaGo.Server.Utilities;
using System;
using System.Collections;
using System.Security;
using System.Threading.Tasks;

namespace BetaGo.Server.Services.Authentication
{
    /// <summary>
    /// A user manager service. Provides access to common operations with users, and abstracts the database
    /// </summary>
    public static class WebUserManager
    {
        public static async Task<RegisteredUser> FindUserByUsernameAsync(string username)
        {
            return await Task.Run(() =>
            {
                RegisteredUser storedUserRecord = null;
                var db = DatabaseAccessService.OpenOrCreateDefault();
                var registeredUsers = db.GetCollection<RegisteredUser>(DatabaseAccessService.UsersCollectionDatabaseKey);
                var userRecord = registeredUsers.FindOne(u => u.Username == username);
                storedUserRecord = userRecord;

                if (storedUserRecord == null)
                {
                    return null;
                }
                return storedUserRecord;
            });
        }

        public static RegisteredUser FindUserByApiKeyAsync(string apiKey)
        {
            RegisteredUser storedUserRecord = null;
            var db = DatabaseAccessService.OpenOrCreateDefault();
            var registeredUsers = db.GetCollection<RegisteredUser>(DatabaseAccessService.UsersCollectionDatabaseKey);
            var userRecord = registeredUsers.FindOne(u => u.ApiKey == apiKey);
            storedUserRecord = userRecord;

            if (storedUserRecord == null)
            {
                return null;
            }
            return storedUserRecord;
        }

        public static bool UpdateUserInDatabase(RegisteredUser currentUser)
        {
            bool result;
            var db = DatabaseAccessService.OpenOrCreateDefault();
            var registeredUsers = db.GetCollection<RegisteredUser>(DatabaseAccessService.UsersCollectionDatabaseKey);
            using (var trans = db.BeginTrans())
            {
                result = registeredUsers.Update(currentUser);
                trans.Commit();
            }
            return result;
        }

        /// <summary>
        /// Attempts to register a new user. Only the username is validated, it is expected that other fields have already been validated!
        /// </summary>
        public static async Task<RegisteredUser> RegisterUserAsync(RegistrationRequest regRequest)
        {
            return await Task.Run(() => RegisterUser(regRequest));
        }

        private static RegisteredUser RegisterUser(RegistrationRequest regRequest)
        {
            RegisteredUser newUserRecord = null;
            if (FindUserByUsernameAsync(regRequest.Username).GetAwaiter().GetResult() != null)
            {
                //BAD! Another conflicting user exists!
                throw new SecurityException("A user with the same username already exists!");
            }
            var db = DatabaseAccessService.OpenOrCreateDefault();
            var registeredUsers = db.GetCollection<RegisteredUser>(DatabaseAccessService.UsersCollectionDatabaseKey);
            using (var trans = db.BeginTrans())
            {
                //Calculate cryptographic info
                var cryptoConf = PasswordCryptoConfiguration.CreateDefault();
                var pwSalt = AuthCryptoHelper.GetRandomSalt(64);
                var encryptedPassword = AuthCryptoHelper.CalculateUserPasswordHash(regRequest.Password, pwSalt, cryptoConf);
                // Create user
                newUserRecord = new RegisteredUser
                {
                    Identifier = Guid.NewGuid().ToString(),
                    Username = regRequest.Username,
                    PhoneNumber = regRequest.PhoneNumber,
                    ApiKey = StringUtils.SecureRandomString(40),
                    CryptoSalt = pwSalt,
                    PasswordCryptoConf = cryptoConf,
                    PasswordKey = encryptedPassword,
                };
                // Add the user to the database
                registeredUsers.Insert(newUserRecord);

                // Index database
                registeredUsers.EnsureIndex(x => x.Identifier);
                registeredUsers.EnsureIndex(x => x.ApiKey);
                registeredUsers.EnsureIndex(x => x.Username);

                trans.Commit();
            }
            return newUserRecord;
        }

        public static async Task<bool> CheckPasswordAsync(string password, RegisteredUser userRecord)
        {
            return await Task.Run(() => CheckPassword(password, userRecord));
        }

        private static bool CheckPassword(string password, RegisteredUser userRecord)
        {
            //Calculate hash and compare
            var pwKey = AuthCryptoHelper.CalculateUserPasswordHash(password, userRecord.CryptoSalt, userRecord.PasswordCryptoConf);
            return StructuralComparisons.StructuralEqualityComparer.Equals(pwKey, userRecord.PasswordKey);
        }
    }
}