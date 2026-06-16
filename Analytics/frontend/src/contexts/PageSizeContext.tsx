import { createContext, useContext, useState, type ReactNode } from 'react';

const PAGE_SIZE_OPTIONS = [5, 10, 20, 50, 100] as const;
const DEFAULT_PAGE_SIZE = 10;

interface PageSizeContextValue {
  pageSize: number;
  setPageSize: (size: number) => void;
  pageSizeOptions: readonly number[];
}

const PageSizeContext = createContext<PageSizeContextValue | undefined>(undefined);

export function PageSizeProvider({ children }: { children: ReactNode }) {
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  return (
    <PageSizeContext.Provider value={{ pageSize, setPageSize, pageSizeOptions: PAGE_SIZE_OPTIONS }}>
      {children}
    </PageSizeContext.Provider>
  );
}

export function usePageSize(): PageSizeContextValue {
  const context = useContext(PageSizeContext);
  if (!context) throw new Error('usePageSize must be used within a PageSizeProvider');
  return context;
}
