import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'app-greeting',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<p>Hello, {{ name }}.</p>',
})
export class GreetingComponent {
  @Input() name = '';
}
