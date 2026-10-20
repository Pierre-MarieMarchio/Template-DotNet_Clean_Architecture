export interface GreetingProps {
  readonly name: string;
}

export function Greeting({ name }: GreetingProps) {
  return <p>Hello, {name}.</p>;
}
