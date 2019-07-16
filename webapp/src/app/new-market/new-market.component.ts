import {Component, OnInit} from '@angular/core';
import {FormControl, FormGroup} from '@angular/forms';
import {MarketService} from '../market.service';

@Component({
  selector: 'app-new-market',
  templateUrl: './new-market.component.html',
  styleUrls: ['./new-market.component.scss']
})
export class NewMarketComponent implements OnInit {
  newMarketForm = new FormGroup({
    marketName: new FormControl(''),
    marketInn: new FormControl(''),
    marketType: new FormControl('')
  });
  marketTypes: Array<string>;

  constructor(private marketService: MarketService) {
    this.marketTypes = ['super_market', 'fuel'];
  }

  ngOnInit() {
  }

  onSubmit() {
    const name = this.newMarketForm.value.marketName;
    const inn = this.newMarketForm.value.marketInn;
    const type = this.newMarketForm.value.marketType;
    this.marketService.addMarket(name, [inn], type).subscribe();
  }
}
