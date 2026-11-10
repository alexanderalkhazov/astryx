// Public, non-secret configuration only (SYSTEM_DESIGN.md §18.8). Never put API keys here.
export const apiUrl = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5080';
