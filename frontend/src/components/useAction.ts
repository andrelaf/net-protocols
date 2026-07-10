import { useCallback, useState } from 'react';
import { ApiError } from '../api/client';

/**
 * Executa uma ação assíncrona guardando resultado, erro e estado de carregamento.
 *
 * Evita que cada aba reescreva o mesmo try/catch/setLoading. Erros de `ApiError` já vêm
 * com a mensagem do ProblemDetails do backend — que, no caso de broker fora do ar, diz
 * exatamente qual comando rodar.
 */
export function useAction<TArgs extends unknown[], TResult>(
  action: (...args: TArgs) => Promise<TResult>,
) {
  const [result, setResult] = useState<TResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);

  const run = useCallback(
    async (...args: TArgs) => {
      setRunning(true);
      setError(null);
      try {
        setResult(await action(...args));
      } catch (caught) {
        setResult(null);
        setError(
          caught instanceof ApiError
            ? caught.message
            : caught instanceof Error
              ? `Não foi possível falar com o gateway: ${caught.message}`
              : 'Erro desconhecido.',
        );
      } finally {
        setRunning(false);
      }
    },
    [action],
  );

  return { run, result, error, running };
}
