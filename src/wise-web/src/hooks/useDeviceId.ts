import { useMemo } from 'react';

function generateId(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = Math.random() * 16 | 0;
    return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
  });
}

export function useDeviceId(): string {
  return useMemo(() => {
    if (typeof window === 'undefined') return 'server';
    const stored = localStorage.getItem('wise-device-id');
    if (stored) return stored;
    const id = generateId();
    localStorage.setItem('wise-device-id', id);
    return id;
  }, []);
}
