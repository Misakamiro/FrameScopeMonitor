declare module "@preview/native-core" {
  export function invoke<T>(command: string, args?: Record<string, unknown>): Promise<T>;
  export function convertFileSrc(filePath: string, protocol?: string): string;
}
