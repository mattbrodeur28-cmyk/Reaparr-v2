const DB_NAME = 'reaparr-persistent-cache';
const DB_VERSION = 1;
const POSTER_STORE = 'posters';
const VALUE_STORE = 'values';
const MAX_POSTER_ENTRIES = 750;

interface IPosterCacheRecord {
	key: string;
	blob: Blob;
	cachedAt: number;
}

interface IValueCacheRecord<T> {
	key: string;
	value: T;
	cachedAt: number;
}

export interface ICachedValue<T> {
	value: T;
	cachedAt: number;
	isFresh: boolean;
}

let dbPromise: Promise<IDBDatabase | null> | null = null;
let posterWritesSincePrune = 0;

function openDatabase(): Promise<IDBDatabase | null> {
	if (typeof indexedDB === 'undefined') {
		return Promise.resolve(null);
	}

	if (dbPromise) {
		return dbPromise;
	}

	dbPromise = new Promise((resolve) => {
		const request = indexedDB.open(DB_NAME, DB_VERSION);

		request.onupgradeneeded = () => {
			const db = request.result;

			if (!db.objectStoreNames.contains(POSTER_STORE)) {
				const posterStore = db.createObjectStore(POSTER_STORE, { keyPath: 'key' });
				posterStore.createIndex('cachedAt', 'cachedAt');
			}

			if (!db.objectStoreNames.contains(VALUE_STORE)) {
				db.createObjectStore(VALUE_STORE, { keyPath: 'key' });
			}
		};

		request.onsuccess = () => resolve(request.result);
		request.onerror = () => resolve(null);
		request.onblocked = () => resolve(null);
	});

	return dbPromise;
}

function getRecord<T>(storeName: string, key: string): Promise<T | null> {
	return openDatabase().then((db) => {
		if (!db) {
			return null;
		}

		return new Promise<T | null>((resolve) => {
			const tx = db.transaction(storeName, 'readonly');
			const request = tx.objectStore(storeName).get(key);
			request.onsuccess = () => resolve((request.result as T | undefined) ?? null);
			request.onerror = () => resolve(null);
		});
	});
}

function putRecord(storeName: string, value: unknown): Promise<void> {
	return openDatabase().then((db) => {
		if (!db) {
			return;
		}

		return new Promise<void>((resolve) => {
			const tx = db.transaction(storeName, 'readwrite');
			tx.objectStore(storeName).put(value);
			tx.oncomplete = () => resolve();
			tx.onerror = () => resolve();
			tx.onabort = () => resolve();
		});
	});
}

export async function getCachedPosterBlob(key: string, maxAgeMs: number): Promise<Blob | null> {
	const record = await getRecord<IPosterCacheRecord>(POSTER_STORE, key);
	if (!record) {
		return null;
	}

	if (Date.now() - record.cachedAt > maxAgeMs) {
		return null;
	}

	return record.blob;
}

export async function setCachedPosterBlob(key: string, blob: Blob): Promise<void> {
	await putRecord(POSTER_STORE, {
		key,
		blob,
		cachedAt: Date.now(),
	} satisfies IPosterCacheRecord);

	posterWritesSincePrune++;
	if (posterWritesSincePrune >= 25) {
		posterWritesSincePrune = 0;
		void prunePosterCache();
	}
}

export async function getCachedValue<T>(
	key: string,
	maxAgeMs: number,
	allowStale = false,
): Promise<ICachedValue<T> | null> {
	const record = await getRecord<IValueCacheRecord<T>>(VALUE_STORE, key);
	if (!record) {
		return null;
	}

	const isFresh = Date.now() - record.cachedAt <= maxAgeMs;
	if (!isFresh && !allowStale) {
		return null;
	}

	return {
		value: record.value,
		cachedAt: record.cachedAt,
		isFresh,
	};
}

export async function setCachedValue<T>(key: string, value: T): Promise<void> {
	await putRecord(VALUE_STORE, {
		key,
		value,
		cachedAt: Date.now(),
	} satisfies IValueCacheRecord<T>);
}

async function prunePosterCache(): Promise<void> {
	const db = await openDatabase();
	if (!db) {
		return;
	}

	const count = await new Promise<number>((resolve) => {
		const tx = db.transaction(POSTER_STORE, 'readonly');
		const request = tx.objectStore(POSTER_STORE).count();
		request.onsuccess = () => resolve(request.result);
		request.onerror = () => resolve(0);
	});

	const removeCount = count - MAX_POSTER_ENTRIES;
	if (removeCount <= 0) {
		return;
	}

	await new Promise<void>((resolve) => {
		const tx = db.transaction(POSTER_STORE, 'readwrite');
		const store = tx.objectStore(POSTER_STORE);
		const index = store.index('cachedAt');
		const cursorRequest = index.openCursor();
		let removed = 0;

		cursorRequest.onsuccess = () => {
			const cursor = cursorRequest.result;
			if (!cursor || removed >= removeCount) {
				return;
			}

			cursor.delete();
			removed++;
			cursor.continue();
		};

		tx.oncomplete = () => resolve();
		tx.onerror = () => resolve();
		tx.onabort = () => resolve();
	});
}
