import {NgModule} from '@angular/core';
import {Routes, RouterModule} from '@angular/router';
import {ReceiptsComponent} from './receipts/receipts.component';
import {MarketsComponent} from './markets/markets.component';

const routes: Routes = [
  {path: '', redirectTo: '/receipts', pathMatch: 'full'},
  {path: 'receipts', component: ReceiptsComponent},
  {path: 'markets', component: MarketsComponent}
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule {
}
