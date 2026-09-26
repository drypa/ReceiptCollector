import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchReceiptDetails, isNotFound } from '../api/receipts';
import type { ReceiptDetails } from '../types/receipt';

/** Загрузка карточки чека по id из URL: состояния те же, что у списков (ADR-023, F3). */
export function useReceiptDetails(receiptId: string) {
  const [receipt, setReceipt] = useState<ReceiptDetails | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const load = useCallback(() => {
    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setIsLoading(true);
    setError(null);
    setNotFound(false);

    fetchReceiptDetails(receiptId, controller.signal)
      .then((details) => {
        setReceipt(details);
      })
      .catch((fetchError) => {
        if (fetchError instanceof DOMException && fetchError.name === 'AbortError') {
          return;
        }
        if (isNotFound(fetchError)) {
          setNotFound(true);
          setReceipt(null);
          return;
        }
        setReceipt(null);
        setError(fetchError instanceof Error ? fetchError.message : 'Неизвестная ошибка');
      })
      .finally(() => {
        if (abortRef.current === controller) {
          setIsLoading(false);
        }
      });
  }, [receiptId]);

  useEffect(() => {
    load();

    return () => {
      abortRef.current?.abort();
    };
  }, [load]);

  return { receipt, isLoading, notFound, error, refresh: load };
}
