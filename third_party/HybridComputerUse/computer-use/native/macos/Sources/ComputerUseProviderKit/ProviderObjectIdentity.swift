import ApplicationServices
import Foundation

// Hashes select buckets only. Retained objects and equality establish identity.
//
// The registry holds at most `capacity` identities and evicts the least
// recently used ones, so a long session does not end in a provider restart.
// A token is never issued twice: an evicted object gets a new token when a
// snapshot meets it again, and a ref holding the old token resolves to no
// object and fails as stale_element_ref, never as another element. An
// identity bound or matched within the last `protectedBinds` binds is never
// evicted, so the reads around one action share their tokens and a recent
// element cannot look absent. When those alone exceed the capacity, bind
// still fails with providerResetRequired.
final class ProviderObjectIdentityRegistry<Object>: @unchecked Sendable {
    private struct Bucket: Hashable {
        let scope: String
        let hash: UInt
    }

    private struct Entry {
        let object: Object
        let token: String
        // The bind that last bound or matched this identity.
        var lastUse: UInt64
    }

    private let lock = NSLock()
    private let epoch = UUID().uuidString
    private let capacity: Int
    private let protectedBinds: UInt64
    private let hash: (Object) -> UInt
    private let equal: (Object, Object) -> Bool
    private var buckets: [Bucket: [Entry]] = [:]
    private var count = 0
    private var issued = 0
    private var binds: UInt64 = 0

    init(
        capacity: Int = 65_536,
        protectedBinds: Int = 4,
        hash: @escaping (Object) -> UInt,
        equal: @escaping (Object, Object) -> Bool
    ) {
        self.capacity = capacity
        self.protectedBinds = UInt64(max(1, protectedBinds))
        self.hash = hash
        self.equal = equal
    }

    var identityCount: Int {
        lock.lock()
        defer { lock.unlock() }
        return count
    }

    func bind(_ objects: [Object], scope: String) throws -> [String] {
        lock.lock()
        defer { lock.unlock() }
        let use = binds + 1
        var pending: [Bucket: [Entry]] = [:]
        var matched: [Bucket: Set<String>] = [:]
        var added = 0
        var comparisons = 0
        var tokens: [String] = []
        for object in objects {
            let key = Bucket(scope: scope, hash: hash(object))
            var match: String?
            for entries in [buckets[key] ?? [], pending[key] ?? []] {
                for entry in entries {
                    comparisons += 1
                    guard comparisons <= 131_072 else {
                        throw ComputerUseError.providerResetRequired(
                            "Native object identity comparison budget exhausted; restart the provider before another request."
                        )
                    }
                    if equal(entry.object, object) {
                        match = entry.token
                        break
                    }
                }
                if match != nil { break }
            }
            if let match {
                matched[key, default: []].insert(match)
                tokens.append(match)
                continue
            }
            added += 1
            let token = "ax:\(epoch):\(issued + added)"
            pending[key, default: []].append(Entry(object: object, token: token, lastUse: use))
            tokens.append(token)
        }
        if count + added > capacity {
            try evict(count + added - capacity, sparing: matched, before: use)
        }
        // Failed batches publish neither partial identities nor consumed IDs.
        for (key, used) in matched {
            guard var entries = buckets[key] else { continue }
            for index in entries.indices where used.contains(entries[index].token) {
                entries[index].lastUse = use
            }
            buckets[key] = entries
        }
        for (key, entries) in pending {
            buckets[key, default: []].append(contentsOf: entries)
        }
        count += added
        issued += added
        binds = use
        return tokens
    }

    // Evicts at least `needed` identities, least recently used first, and
    // down to seven eighths of the capacity when it can, so that a full
    // registry does not sweep its entries on every bind. Nothing changes
    // when fewer than `needed` identities may be evicted.
    private func evict(
        _ needed: Int, sparing matched: [Bucket: Set<String>], before use: UInt64
    ) throws {
        func evictable(_ key: Bucket, _ entry: Entry) -> Bool {
            use - entry.lastUse >= protectedBinds &&
                matched[key]?.contains(entry.token) != true
        }
        var ages: [UInt64] = []
        ages.reserveCapacity(count)
        for (key, entries) in buckets {
            for entry in entries where evictable(key, entry) {
                ages.append(entry.lastUse)
            }
        }
        guard ages.count >= needed else {
            throw ComputerUseError.providerResetRequired(
                "Native object identity capacity exhausted; restart the provider before another request."
            )
        }
        let target = min(ages.count, needed + capacity / 8)
        ages.sort()
        // Evict every identity last used before the cutoff bind, then as
        // many of the cutoff bind's identities as the target still needs.
        let cutoff = ages[target - 1]
        var atCutoff = target - ages.prefix(while: { $0 < cutoff }).count
        var removed = 0
        for key in Array(buckets.keys) {
            buckets[key]?.removeAll { entry in
                guard entry.lastUse <= cutoff, evictable(key, entry) else { return false }
                if entry.lastUse == cutoff {
                    guard atCutoff > 0 else { return false }
                    atCutoff -= 1
                }
                removed += 1
                return true
            }
            if buckets[key]?.isEmpty == true {
                buckets[key] = nil
            }
        }
        count -= removed
    }

    func matchesRetained(_ object: Object, token: String, scope: String) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        let key = Bucket(scope: scope, hash: hash(object))
        guard var entries = buckets[key],
              let index = entries.firstIndex(where: { $0.token == token }),
              equal(entries[index].object, object), equal(object, entries[index].object)
        else {
            return false
        }
        // A ref that resolves is in use: keep it as long as the last snapshot.
        entries[index].lastUse = max(entries[index].lastUse, binds)
        buckets[key] = entries
        return true
    }
}

private let macProviderObjectRegistry = ProviderObjectIdentityRegistry<AXUIElement>(
    hash: { CFHash($0) },
    equal: { CFEqual($0, $1) }
)

func matchesRetainedMacProviderObject(
    _ object: AXUIElement, token: String, scope: String,
    registry: ProviderObjectIdentityRegistry<AXUIElement>? = nil
) -> Bool {
    (registry ?? macProviderObjectRegistry).matchesRetained(
        object, token: token, scope: scope
    )
}

func bindMacProviderObjectIdentities(
    _ records: [ElementRecord],
    scope: String,
    registry: ProviderObjectIdentityRegistry<AXUIElement>? = nil
) throws {
    let real = records.filter { !$0.isSyntheticText && $0.element != nil }
        .sorted { $0.index < $1.index }
    guard real.allSatisfy({ $0.boundProviderObjectPath == nil }) else {
        throw ComputerUseError.stateUnavailable(
            "Native object identities were already bound; snapshot rejected."
        )
    }
    let tokens = try (registry ?? macProviderObjectRegistry).bind(
        real.compactMap(\.element), scope: scope
    )
    for (record, token) in zip(real, tokens) {
        record.bindProviderObjectPath(token)
    }
}
