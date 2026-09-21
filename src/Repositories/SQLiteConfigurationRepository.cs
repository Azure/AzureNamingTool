using AzureNamingTool.Data;
using AzureNamingTool.Repositories.Interfaces;
using AzureNamingTool.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AzureNamingTool.Repositories
{
    /// <summary>
    /// SQLite implementation of the configuration repository.
    /// Provides data access using Entity Framework Core with optional caching.
    /// </summary>
    /// <typeparam name="T">The entity type</typeparam>
    public class SQLiteConfigurationRepository<T> : IConfigurationRepository<T> where T : class
    {
        private readonly ConfigurationDbContext _dbContext;
        private readonly ICacheService _cacheService;
        private readonly string _cacheKey;
        private readonly DbSet<T> _dbSet;

        /// <summary>
        /// Initializes a new instance of the SQLiteConfigurationRepository
        /// </summary>
        /// <param name="dbContext">Database context for EF Core operations</param>
        /// <param name="cacheService">Cache service for performance optimization</param>
        public SQLiteConfigurationRepository(ConfigurationDbContext dbContext, ICacheService cacheService)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _dbSet = _dbContext.Set<T>();
            _cacheKey = $"SQLite_{typeof(T).Name}_All";
        }

        /// <summary>
        /// Gets all entities of type T
        /// </summary>
        public async Task<IEnumerable<T>> GetAllAsync()
        {
            // Try to get from cache first
            var cachedData = _cacheService.GetCacheObject<List<T>>(_cacheKey);
            if (cachedData != null)
            {
                return cachedData;
            }

            // Load from database
            var data = await _dbSet.AsNoTracking().ToListAsync();

            // Cache the result
            _cacheService.SetCacheObject(_cacheKey, data.ToList());

            return data;
        }

        /// <summary>
        /// Gets a single entity by ID
        /// </summary>
        public async Task<T?> GetByIdAsync(int id)
        {
            // Try cache first
            var allItems = await GetAllAsync();
            var idProperty = typeof(T).GetProperty("Id");
            if (idProperty != null)
            {
                var item = allItems.FirstOrDefault(x =>
                {
                    var itemId = idProperty.GetValue(x);
                    return itemId != null && Convert.ToInt64(itemId) == id;
                });

                if (item != null)
                {
                    return item;
                }
            }

            // If not in cache, try database directly
            return await _dbSet.FindAsync((long)id);
        }

        /// <summary>
        /// Saves a single item (creates or updates)
        /// </summary>
        public async Task SaveAsync(T entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var idProperty = typeof(T).GetProperty("Id");
            if (idProperty == null)
            {
                throw new InvalidOperationException($"Type {typeof(T).Name} does not have an Id property");
            }

            var idValue = idProperty.GetValue(entity);
            var existing = await _dbSet.FindAsync(idValue);

            if (existing != null)
            {
                _dbContext.Entry(existing).CurrentValues.SetValues(entity);
            }
            else
            {
                await _dbSet.AddAsync(entity);
            }

            await _dbContext.SaveChangesAsync();

            // Invalidate cache
            _cacheService.InvalidateCacheObject(_cacheKey);
        }

        /// <summary>
        /// Deletes an entity by ID
        /// </summary>
        public async Task DeleteAsync(int id)
        {
            var entity = await _dbSet.FindAsync((long)id);
            if (entity != null)
            {
                _dbSet.Remove(entity);
                await _dbContext.SaveChangesAsync();

                // Invalidate cache
                _cacheService.InvalidateCacheObject(_cacheKey);
            }
        }

        /// <summary>
        /// Checks if an entity with the given ID exists
        /// </summary>
        public async Task<bool> ExistsAsync(int id)
        {
            var entity = await GetByIdAsync(id);
            return entity != null;
        }

        /// <summary>
        /// Saves all entities (replaces all existing data)
        /// </summary>
        public async Task SaveAllAsync(IEnumerable<T> entities)
        {
            if (entities == null)
            {
                throw new ArgumentNullException(nameof(entities));
            }

            // Begin transaction for atomic operation
            using var transaction = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                // Remove all existing entities and persist the delete first.
                // Saving delete+insert in one batch can violate UNIQUE constraints
                // when replacement entities reuse the same primary keys.
                var existingEntities = await _dbSet.ToListAsync();
                _dbSet.RemoveRange(existingEntities);
                await _dbContext.SaveChangesAsync();

                // Add replacement entities and persist insert in a second batch.
                await _dbSet.AddRangeAsync(entities);
                await _dbContext.SaveChangesAsync();

                // Commit changes
                await transaction.CommitAsync();

                // Invalidate cache
                _cacheService.InvalidateCacheObject(_cacheKey);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException("Failed to save all entities", ex);
            }
        }
    }
}
